# SPDX-License-Identifier: MIT
"""Retime an existing silent gaze reaction without changing image/audio bytes."""
import argparse
import json
import math
from pathlib import Path
import zipfile


def main():
    parser = argparse.ArgumentParser(description="调整兼容主题包的注视停留，保留原包。")
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--hold-seconds', type=float, default=1.2)
    args = parser.parse_args()
    if args.input.resolve() == args.output.resolve():
        parser.error('输出必须与输入不同，以保留原包。')
    if not math.isfinite(args.hold_seconds) or not 0 <= args.hold_seconds <= 20:
        parser.error('停留时间应为 0～20 秒。')
    with zipfile.ZipFile(args.input) as source:
        manifest = json.loads(source.read('manifest.json'))
        action = next((a for a in manifest['actions'] if a['id'] == manifest.get('headReactionAction')), None)
        if not action or action.get('audio') or not manifest.get('headReactionSeamless'):
            parser.error('仅支持首尾待机一致且不带独立音频的按头反应。')
        old_count, fps = action['frames'], action['fps']
        if not math.isfinite(fps) or not 1 <= fps <= 120 or old_count < 23:
            parser.error('原反应帧数或帧率不支持。')
        prefix = action['prefix']
        frame = lambda n: source.read(prefix + f'{n:04d}.png')
        idle = source.read('idle.png')
        if frame(0) != idle or frame(old_count - 1) != idle:
            parser.error('原反应首末帧与待机不一致。')
        hold_count = round(args.hold_seconds * fps)
        frames = [idle] + [frame(n) for n in range(1, 11)] + [frame(11)] * hold_count
        frames += [frame(n) for n in range(old_count - 11, old_count - 1)] + [idle]
        total = sum(a['frames'] for a in manifest['actions']) - old_count + len(frames)
        if len(frames) > 2000 or total > 5000:
            parser.error('修改后帧数超过主题格式限制。')
        action['frames'] = len(frames)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(args.output, 'x', zipfile.ZIP_STORED) as output:
            for name in source.namelist():
                if name != 'manifest.json' and not name.startswith(prefix):
                    output.writestr(name, source.read(name))
            for n, data in enumerate(frames):
                output.writestr(prefix + f'{n:04d}.png', data)
            output.writestr('manifest.json', json.dumps(manifest, ensure_ascii=False, indent=2).encode('utf-8'))
    print(f'注视停留 {hold_count / fps:.3f} 秒；反应总长 {len(frames) / fps:.3f} 秒。')


if __name__ == '__main__':
    main()
