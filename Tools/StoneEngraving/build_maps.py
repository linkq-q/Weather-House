"""Deterministic technical maps from an approved, black-on-white carving stencil.

This converts shape data to height/normal/cavity data; it does not redraw the art.
Usage: python build_maps.py --spec Tools/StoneEngraving/rain_water.json
"""
import argparse
import hashlib
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image


def sparse_chips(base, chipped, probability, seed, cell_px=48):
    """Select sparse patches of the AI damage candidate; retain the approved paths elsewhere."""
    if base.shape!=chipped.shape or not 0<=probability<=1:
        raise ValueError('Matching masks and a probability in [0,1] are required.')
    if probability==0:return base.copy()
    rng=np.random.default_rng(seed)
    cells=rng.random((math.ceil(base.shape[0]/cell_px),math.ceil(base.shape[1]/cell_px)))<probability
    yy,xx=np.indices(base.shape)
    edge=np.minimum(np.minimum(xx%cell_px,cell_px-1-xx%cell_px),
                    np.minimum(yy%cell_px,cell_px-1-yy%cell_px))
    fade=np.clip(edge/6.,0,1);fade=fade*fade*(3-2*fade)
    blend=cells[yy//cell_px,xx//cell_px]*fade
    return (base*(1-blend)+chipped*blend).astype(np.float32)


def compose_layout(art, rain, water, art_offset_y=0, rain_y=None, glyph_height=None):
    """Keep the artwork untouched in the centre and project exact glyph alpha above/below."""
    height,width=art.shape
    if width<=height:raise ValueError('The centre artwork must be landscape.')
    margin=(width-height)//2
    result=np.zeros((width,width),np.float32)
    result[margin+art_offset_y:margin+art_offset_y+height]=art
    for glyph,top in ((rain,0),(water,margin+height)):
        yy,xx=np.where(glyph>.001)
        if not len(xx):raise ValueError('Empty glyph alpha.')
        crop=glyph[yy.min():yy.max()+1,xx.min():xx.max()+1]
        scale=min((glyph_height or margin*.94)/crop.shape[0],width*.18/crop.shape[1])
        w,h=max(1,round(crop.shape[1]*scale)),max(1,round(crop.shape[0]*scale))
        resized=np.clip(np.asarray(Image.fromarray(crop).resize((w,h),Image.Resampling.LANCZOS)),0,1)
        x=(width-w)//2;y=rain_y if top==0 and rain_y is not None else top+(margin-h)//2
        result[y:y+h,x:x+w]=resized
    return result


def stencil_coverage(path):
    return np.clip((.80-np.asarray(Image.open(path).convert('L'),np.float32)/255.)/.60,0,1)


def make_maps(coverage, width_m, depth_m, bevel_px):
    if coverage.ndim != 2 or min(coverage.shape) < 3:
        raise ValueError('Stencil must be a two-dimensional image at least 3x3.')
    if width_m <= 0 or depth_m <= 0 or bevel_px < 1:
        raise ValueError('Width, depth and bevel must be positive.')
    coverage = np.clip(coverage.astype(np.float32), 0, 1)
    inside = coverage > .5
    distance = np.where(inside, bevel_px + 1., 0.).astype(np.float32)
    # Capped octile distance: sufficient for the bevel; deep interiors stay flat.
    for _ in range(math.ceil(bevel_px) + 1):
        padded = np.pad(distance, 1, constant_values=0)
        next_distance = distance.copy()
        for dy, dx, cost in ((-1, 0, 1), (1, 0, 1), (0, -1, 1), (0, 1, 1),
                             (-1, -1, math.sqrt(2)), (-1, 1, math.sqrt(2)),
                             (1, -1, math.sqrt(2)), (1, 1, math.sqrt(2))):
            neighbour = padded[1+dy:1+dy+distance.shape[0], 1+dx:1+dx+distance.shape[1]]
            np.minimum(next_distance, neighbour + cost, out=next_distance)
        distance = next_distance
    profile = np.minimum(distance / bevel_px, 1) * coverage
    height = -depth_m * profile
    pixel_m = width_m / coverage.shape[1]
    dy, dx = np.gradient(height, pixel_m)
    # PNG rows run downward; Unity texture V and tangent-space Y point upward.
    normals = np.stack((-dx, dy, np.ones_like(dx)), axis=-1)
    normals /= np.linalg.norm(normals, axis=-1, keepdims=True)
    cavity = 1 - .52 * profile
    packed = np.stack((profile, cavity, np.zeros_like(profile), coverage), axis=-1)
    return height, normals, packed


def build(spec_path):
    root = Path(__file__).resolve().parents[2]
    spec = json.loads(Path(spec_path).read_text(encoding='utf-8-sig'))
    source = root / spec['source']
    output = root / spec['output']
    output.mkdir(parents=True, exist_ok=True)
    # Threshold with a narrow antialias ramp removes the generated background tone.
    coverage=stencil_coverage(source)
    chipped=stencil_coverage(root/spec['chipped_source'])
    glyphs={key:np.asarray(Image.open(root/path).convert('RGBA'),np.float32)[...,3]/255.
            for key,path in spec['glyphs'].items()}
    layout=spec.get('layout',{})
    coverage=compose_layout(coverage,glyphs['rain'],glyphs['water'],**layout)
    # A small technical expansion supplies candidate chips for glyph edges without redrawing their shapes.
    rough_glyphs={}
    for key,glyph in glyphs.items():
        padded=np.pad(glyph,5);rough=glyph.copy()
        for oy,ox in ((-4,0),(4,0),(0,-4),(0,4),(-3,3),(3,-3)):
            rough=np.maximum(rough,padded[5+oy:5+oy+glyph.shape[0],5+ox:5+ox+glyph.shape[1]])
        rough_glyphs[key]=rough
    chipped=compose_layout(chipped,rough_glyphs['rain'],rough_glyphs['water'],**layout)
    intact=coverage.copy()
    coverage=sparse_chips(coverage,chipped,spec['chip_probability'],spec['chip_seed'])
    h, n, packed = make_maps(coverage, spec['width_m'], spec['depth_m'], spec['bevel_px'])
    Image.fromarray(np.rint((n * .5 + .5) * 255).astype(np.uint8)).save(output / 'RainWater_Normal.png')
    Image.fromarray(np.rint(packed * 255).astype(np.uint8)).save(output / 'RainWater_Mask.png')
    Image.fromarray(np.rint((1 + h / spec['depth_m']) * 65535).astype(np.uint16)).save(output / 'RainWater_Height.png')
    report = dict(spec, source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                  size=[coverage.shape[1],coverage.shape[0]], coverage_fraction=float((coverage > .5).mean()),
                  changed_pixels_fraction=float((abs(coverage-intact)>.01).mean()),
                  chipped_source_sha256=hashlib.sha256((root/spec['chipped_source']).read_bytes()).hexdigest(),
                  glyph_sha256={key:hashlib.sha256((root/path).read_bytes()).hexdigest() for key,path in spec['glyphs'].items()},
                  min_height_m=float(h.min()), max_height_m=float(h.max()),
                  finite=bool(np.isfinite(n).all()),
                  normal_convention='tangent +Y up; neutral (128,128,255)',
                  mask_channels='R groove depth, G cavity AO, B reserved=0, A groove coverage')
    (output / 'maps-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--spec', required=True)
    build(parser.parse_args().spec)
