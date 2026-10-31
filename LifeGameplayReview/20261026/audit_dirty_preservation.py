"""Read-only audit of the original dirty files and an explicitly authorized map Save.

Prints JSON to stdout; never writes the baseline, scene, receipt or any other file.
Without --save-result, the new schema remains RECORDED pending a real Save receipt.
"""
from pathlib import Path
from datetime import datetime, timezone
import argparse
import hashlib
import json

SCHEMA = 'dirty-preservation-authorized-scene-v2'
MAIN_SCENE = 'Assets/Scenes/WeatherHouse_WatersideV2.unity'
SAVE_PASS = 'PASS_SAVED_WITH_BYTE_EXACT_ORIGINAL_BACKUP_VISUAL_PLAY_AND_HUMAN_ACCEPTANCE_SEPARATE'
PRESAVE_PASS = 'PASS_STATE_GUARDS_COORDINATOR_SAVE_REVIEW_PENDING'


def digest(path):
    with Path(path).open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def resolve(root, path):
    result = Path(path)
    return result if result.is_absolute() else root / result


def same_hash(a, b):
    return (isinstance(a, str) and isinstance(b, str) and len(a) == 64
            and all(c in '0123456789abcdefABCDEF' for c in a) and a.lower() == b.lower())


def audit(root, save_result=None, authorization=''):
    root = Path(root).resolve()
    baseline_path = root / 'LifeGameplayReview/20261026/initial-dirty-files.json'
    result = {'schemaVersion': SCHEMA, 'utc': datetime.now(timezone.utc).isoformat(),
              'status': 'RECORDED', 'baselinePath': str(baseline_path), 'files': [],
              'authorizedScene': None, 'checks': [],
              'boundary': 'Read-only SHA audit and linked Unity Save/preSave receipts. Unity protected/descendant guards are recorded verifier evidence, not an offline rerun. Visual, Play and human acceptance are separate.'}

    def check(name, passed, detail=''):
        result['checks'].append({'name': name, 'status': 'PASS' if passed else 'FAIL', 'detail': detail})

    try:
        baseline = read(baseline_path)
        result['baselineSha256'] = digest(baseline_path)
        paths = [row['path'] for row in baseline]
        check('original16_baseline', len(paths) == 16 and len(set(paths)) == 16 and paths.count(MAIN_SCENE) == 1)
        hashes = {row['path']: row['sha256'] for row in baseline}
        for row in baseline:
            current = digest(root / row['path'])
            record = {'path': row['path'], 'initialSha256': row['sha256'],
                      'currentSha256': current, 'unchanged': same_hash(current, row['sha256'])}
            if row['path'] == MAIN_SCENE:
                result['authorizedScene'] = record
            else:
                result['files'].append(record)
        check('other15_sha_unchanged', len(result['files']) == 15 and all(row['unchanged'] for row in result['files']))
        if save_result is None:
            result['detail'] = 'Save receipt not supplied; no authorized scene PASS is generated.'
            if any(item['status'] == 'FAIL' for item in result['checks']):
                result['status'] = 'FAIL'
            return result

        receipt_path = resolve(root, save_result).resolve()
        receipt = read(receipt_path)
        scene = result['authorizedScene']
        scene.update({'authorization': authorization, 'saveReceiptPath': str(receipt_path),
                      'saveReceiptSha256': digest(receipt_path)})
        check('explicit_main_scene_authorization', bool(authorization.strip()) and receipt.get('scene') == MAIN_SCENE)
        check('actual_save_pass', receipt.get('status') == SAVE_PASS and receipt.get('saveCalled') is True
              and receipt.get('saveReturned') is True and receipt.get('dirtyBefore') is True
              and receipt.get('dirtyAfter') is False and not receipt.get('error'))
        initial = hashes[MAIN_SCENE]
        backup = resolve(root, receipt['backupPath']).resolve()
        scene.update({'backupPath': str(backup), 'backupSha256': digest(backup),
                      'originalBytes': receipt.get('originalBytes'), 'backupBytes': backup.stat().st_size})
        check('original_scene_byte_backup', all(same_hash(initial, value) for value in
              [receipt.get('originalSceneSha256'), receipt.get('backupSha256'), scene['backupSha256']])
              and scene['backupBytes'] == receipt.get('backupBytes') == receipt.get('originalBytes')
              and scene['backupBytes'] > 0)
        check('saved_scene_current_sha', same_hash(scene['currentSha256'], receipt.get('savedSceneSha256'))
              and scene['unchanged'] is False and (root / MAIN_SCENE).stat().st_size == receipt.get('savedBytes'))
        check('save_memory_guard', same_hash(receipt.get('originalMemoryHash'), receipt.get('savedMemoryHash'))
              and receipt.get('originalTransforms', 0) > 0
              and receipt.get('originalTransforms') == receipt.get('savedTransforms'))

        guard_path = resolve(root, receipt['preSaveGuard']).resolve()
        guard = read(guard_path)
        scene.update({'preSaveGuardPath': str(guard_path), 'preSaveGuardSha256': digest(guard_path)})
        check('preSave_guard', guard.get('status') == PRESAVE_PASS and guard.get('playing') is False
              and guard.get('dirty') is True and same_hash(initial, guard.get('sceneSha256')))
        apply_path = resolve(root, guard['receipt']).resolve()
        applied = read(apply_path)
        scene.update({'applyReceiptPath': str(apply_path), 'applyReceiptSha256': digest(apply_path)})
        check('reviewed_apply_receipt_hash', same_hash(scene['applyReceiptSha256'], receipt.get('receiptSha256'))
              and applied.get('version') == 'night-map-reuse-v2'
              and applied.get('status') == 'APPLIED_UNSAVED_VISUAL_AND_PLAY_PENDING'
              and applied.get('dirtyBefore') is False and same_hash(initial, applied.get('sceneSha256')))
        roots = applied.get('protectionRoots', [])
        before = applied.get('before', [])
        additions = applied.get('addedTransforms', 0) + (1 if applied.get('additions') else 0)
        linked = (guard.get('status') == PRESAVE_PASS
                  and same_hash(scene['applyReceiptSha256'], receipt.get('receiptSha256')))
        check('protected_guard_receipt', linked and len(roots) > 0 and guard.get('protectionRoots') == len(roots),
              'VerifyForSave completed GuardBaseline and CheckDescendants, including protected spatial descendants.')
        check('descendant_guard_receipt', linked and len(before) > 0 and guard.get('originalTransforms') == len(before)
              and guard.get('addedTransforms') == additions
              and receipt.get('originalTransforms') == len(before) + additions,
              'Recorded Unity verifier success; this script does not reconstruct Unity geometry.')
        check('binding_counts_receipt', guard.get('rendererCount') == applied.get('rendererCount', 0) + applied.get('addedRenderers', 0)
              and guard.get('colliderCount') == applied.get('colliderCount', 0) + applied.get('addedColliders', 0))
        check('terrain_source_sha', same_hash(digest(resolve(root, applied['terrainPath'])), applied.get('terrainSha256'))
              and same_hash(applied.get('terrainSha256'), receipt.get('terrainSha256')))
        result['status'] = 'PASS' if all(item['status'] == 'PASS' for item in result['checks']) else 'FAIL'
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        check('readable_complete_evidence', False, str(error))
        result['status'] = 'FAIL'
    return result


def summarize(data, root):
    """Old list/dictionary records stay compatible; only v2 permits changed scene bytes."""
    if isinstance(data, list) or isinstance(data, dict) and 'schemaVersion' not in data:
        rows = data if isinstance(data, list) else data.get('files', [])
        if not isinstance(rows, list):
            return 'FAIL', '原有修改保护的 files 不是列表。'
        valid = bool(rows) and all(isinstance(row, dict) and row.get('unchanged') is True for row in rows)
        if isinstance(data, dict) and data.get('status') != 'PASS':
            valid = False
        live = audit(root)
        expected = {row['path'] for row in live.get('files', [])} | {MAIN_SCENE}
        recorded = [row.get('path') for row in rows if isinstance(row, dict)]
        valid = (valid and live['status'] != 'FAIL' and len(recorded) == 16 and set(recorded) == expected
                 and (live.get('authorizedScene') or {}).get('unchanged') is True)
        return ('PASS' if valid else 'FAIL', f'{len(rows)} 个原有修改文件逐项核对；旧格式须原16项当前 SHA 全部不变，授权场景改动须使用 v2。')
    if not isinstance(data, dict) or data.get('schemaVersion') != SCHEMA:
        return 'FAIL', '不支持的原有修改保护 schema。'
    scene = data.get('authorizedScene') or {}
    if not isinstance(scene, dict):
        return 'FAIL', '授权场景记录不是字典。'
    # Re-read live whitelisted files and receipts. A caller-supplied top-level PASS is insufficient.
    live = audit(root, scene.get('saveReceiptPath'), scene.get('authorization', ''))
    if data.get('status') != 'PASS':
        status = 'RECORDED' if data.get('status') == 'RECORDED' and live['status'] == 'RECORDED' else 'FAIL'
    else:
        status = live['status']
        if not same_hash(data.get('baselineSha256'), live.get('baselineSha256')):
            status = 'FAIL'
        if data.get('files') != live.get('files') or scene.get('unchanged') is not False:
            status = 'FAIL'
        for field in ['path', 'initialSha256', 'currentSha256', 'backupSha256', 'saveReceiptSha256', 'applyReceiptSha256', 'preSaveGuardSha256']:
            if scene.get(field) != (live.get('authorizedScene') or {}).get(field):
                status = 'FAIL'
    failures = [row['name'] for row in live['checks'] if row['status'] == 'FAIL']
    detail = ('15 个原有文件 SHA 不变；主场景为本轮显式授权保存，unchanged=false。'
              '原始场景逐字节备份匹配初始 SHA；实际 Save、protected/descendant/preSave 收据守卫分别核对。'
              '保护通过不等于地图视觉、Play 或人工验收通过。')
    if status != 'PASS':
        detail = '授权场景保护尚未通过：' + ('、'.join(failures) if failures else '缺少实际保存收据或完整审计记录。')
    return status, detail


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--save-result', type=Path)
    parser.add_argument('--authorization', default='')
    args = parser.parse_args()
    print(json.dumps(audit(args.root, args.save_result, args.authorization), ensure_ascii=False, indent=2))
