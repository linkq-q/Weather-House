"""Build a local catalog from the intake manifest and actual Unity reports."""
import collections, html, json, pathlib

ROOT = pathlib.Path(__file__).resolve().parent
PROJECT = ROOT.parent
LABELS = {
    'AncientRuins': ('风格化古代遗迹', '石拱、台阶、石柱、残墙、铺石、树根；优先用于 01/03/04/05/10。'),
    'NatureBundle': ('风格化自然植被 1.5', '带根树干、阔叶树冠、成簇花草、灌木、层叠岸石；保留作者 URP 风动。'),
    'EnchantedForest': ('POLYGON 奇幻森林', '树根、苔石、蕨类、蘑菇、树木、灯架等。部分造型较奇幻，作为局部点缀候选。'),
    'Dreamscapes': ('Dreamscapes 自然物件', '树木、灌丛、蘑菇、石柱、碎石；保留原烘焙树网格及图集，作为静态资源使用，未复原旧树编辑器与旧风动。'),
    'TerrainTextures': ('风格化地表材质', '50 套草地、泥土、岩石、砂地、根系、铺石及季节变化，附原贴图和 TerrainLayer。'),
    'Cliffs': ('山崖与岩石', '12 个山崖/岸石预制体及 LOD，作为边界山体和岸壁形体候选。'),
    'CastleValley': ('峡谷台地模块', '246 个台地、崖壁、岩拱和组合；此包主要是砂岩峡谷，并非房屋建筑包。'),
    'Butterflies': ('低模蝴蝶', '57 种纹理外观，含骨架。飞行/停驻动画尚未提取，本轮为静态资源入库。'),
}

def load(name):
    path = ROOT / name
    return json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else {}

def main():
    manifest = load('import-manifest.json')
    reports = {c:load(c+'-report.json') for c in ['import','adapt','verify','preview']}
    rows=[]; sections=[]; md=[]
    for pack in manifest['packs']:
        key=pack['key'];label,note=LABELS[key]
        files=[r for r in manifest['files'] if f'/{key}/' in r['path']]
        kinds=collections.Counter(pathlib.Path(r['path']).suffix.lower() for r in files)
        count=f"{kinds['.prefab']} 预制体 / {kinds['.fbx']} 模型 / {kinds['.mat']} 材质"
        if key=='TerrainTextures':count+=f" / {kinds['.terrainlayer']} 地形层"
        rows.append(f'<tr><td><a href="#{key}">{html.escape(label)}</a></td><td>{count}</td><td>{html.escape(note)}</td></tr>')
        cards=[]
        for shot in reports['preview'].get('previews',[]):
            if shot['pack']!=key:continue
            image=pathlib.Path(shot['image']).relative_to('AssetIntakeReview').as_posix()
            cards.append(f'<figure><a href="{image}"><img src="{image}" loading="lazy"></a><figcaption>{html.escape(pathlib.Path(shot["asset"]).stem)}<details><summary>项目内路径</summary><code>{html.escape(shot["asset"])}</code></details></figcaption></figure>')
        sections.append(f'<section id="{key}"><h2>{html.escape(label)}</h2><p>{html.escape(note)}</p><div class="cards">{"".join(cards)}</div><p class="source">来源：{html.escape(pack["source"])}</p><p class="source">导入位置：Assets/Environment/DDriveLibrary/{key}</p></section>')
        md.append(f'| {label} | {count} | {note} |')
    states=' · '.join(c+': '+reports[c].get('status','待运行') for c in reports)
    issues=[f'{c}: {i["path"]} — {i["message"]}' for c,r in reports.items() for i in r.get('issues',[]) if i['severity'] in ['error','warning']]
    issuehtml='<details><summary>验证提示</summary><ul>'+''.join('<li>'+html.escape(i)+'</li>' for i in issues)+'</ul></details>' if issues else ''
    page='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>D 盘资产导入库</title><style>
    body{font:16px/1.65 system-ui,"Microsoft YaHei",sans-serif;background:#14231e;color:#e5ecdf;margin:0;padding:32px max(24px,calc((100vw - 1280px)/2))}h1{font-size:32px}h2{margin:0}a{color:#d5ddb0}p{max-width:1000px}.status{padding:16px;background:#2e4537;border-radius:10px}table{border-collapse:collapse;width:100%;margin:28px 0}td,th{padding:12px;text-align:left;border-bottom:1px solid #45604b}td:first-child{min-width:150px}section{padding:28px 0;border-top:1px solid #45604b}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(250px,1fr));gap:16px}figure{margin:0;background:#223b30;border-radius:12px;overflow:hidden}img{width:100%;display:block}figcaption{padding:12px}.source,code{font-size:13px;color:#a8b8a8;overflow-wrap:anywhere}summary{cursor:pointer}li{overflow-wrap:anywhere}footer{padding:24px 0;color:#b9c8ba}
    </style><h1>D 盘资产导入库</h1><p>独立资源库，用于下一轮水岸场景细化。下方缩略图来自本项目 Unity 的实际 URP 渲染，展示资源本体；场景替换的同机位对照另见 <a href="scene-comparison.html">地编材质对照</a>。</p>'''
    page+=f'<p class="status">{html.escape(states)}</p>{issuehtml}<table><thead><tr><th>资源组</th><th>入库数量</th><th>用途与限制</th></tr></thead><tbody>{"".join(rows)}</tbody></table>'+''.join(sections)
    page+='''<section><h2>已复用与尚待补齐</h2><p>Pure Nature 2 Meadows 已在项目 Assets/PureMeadow 完整存在，529/529 路径与 GUID 一致，未重复导入。Nature Bundle 1.3 与 1.5 重复，本轮只选 1.5。</p><p>D 盘读取期间发生断连，重新连接后 exFAT 卷报告 Full Repair Needed。本轮仅使用断连前已完整提取的缓存。Forest Animals 动作、水草 1/5、Nature Bundle 的 4 个 GrassFlower 和 4 个 Grass 预制体所需 PSD、蝴蝶飞行/停驻动画、PBR 材质精选和独立山石尚未完成入库。</p><p>目前仍未找到能直接匹配概念图的苔顶圆窗小屋、带宽阔树冠的空心老树、精细贝壳/玻璃风铃。峡谷和写实山崖的造型可用，色彩与表面细节需要在实际布景时统一。</p></section><footer>仅从用户已有本地包选取资源。原包保留在 D 盘；导入不覆盖既有资产；地编试换单独记录，主草地/树/水的既有调参保持。来源包自带的说明文件随资源保留。</footer></html>'''
    (ROOT/'D盘资产库.html').write_text(page,encoding='utf-8')
    doc='# D 盘资产导入记录\n\n'+states+'\n\n| 资源组 | 数量 | 使用方向与限制 |\n|---|---|---|\n'+'\n'.join(md)+'\n\n'
    doc+='导入目录：`Assets/Environment/DDriveLibrary`。文件来源、GUID、导入前 SHA-256 见 `AssetIntakeReview/import-manifest.json`；Unity 检查见同目录各 `*-report.json`；预览见 `AssetIntakeReview/D盘资产库.html`。\n\n'
    doc+='保留原生 URP 材质；旧 Built-in 材质在独立副本中适配 URP Lit，保留能对应的贴图、颜色、法线与透明裁切。旧自定义材质的特殊效果不因导入自动实现。资源导入不覆盖既有资产；后续只在 WatersideV2 独立地形层/指定低墙做同机位 A/B，见地编材质对照。主草地、水、树的调参保持。\n\n'
    doc+='Pure Nature 已有 529/529 同 GUID 项，避免重复。D 盘掉线后只继续处理完整缓存；后续重新识别时卷状态为 `Warning / Full Repair Needed`，尚未读取的资产留待磁盘状态稳定后续导。没有执行格式化或文件系统修复。\n\n'
    doc+='待补：Forest Animals 动作、水草 1/5、4 个 GrassFlower、4 个 Grass 预制体缺少 PSD、蝴蝶飞行/停驻动画、PBR 材质精选、独立山石。概念精确缺口仍为苔顶圆窗小屋、带冠空心老树、精细风铃零件。\n'
    (PROJECT/'Docs/WatersideV2/D盘资产导入记录.md').write_text(doc,encoding='utf-8')
    print(states)

if __name__=='__main__': main()
