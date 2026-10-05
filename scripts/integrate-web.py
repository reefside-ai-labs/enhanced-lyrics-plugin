#!/usr/bin/env python3
"""Install/remove a reversible loader in server-hosted Jellyfin Web. No bundles change."""
import argparse
from pathlib import Path

START = '<!-- enhanced-lyrics:start -->'
END = '<!-- enhanced-lyrics:end -->'
SNIPPET = START + '<script src="../EnhancedLyrics/Assets/enhanced-lyrics.js"></script>' + END

def update(index: Path, remove: bool):
    content = index.read_text()
    if START in content:
        start = content.index(START)
        end = content.index(END, start) + len(END)
        content = content[:start] + content[end:]
    if not remove:
        position = content.find('<script')
        if position < 0:
            raise ValueError('No Jellyfin scripts found; refusing to modify an unknown index.html')
        content = content[:position] + SNIPPET + content[position:]
    if content != index.read_text():
        backup = index.with_name('index.html.enhanced-lyrics-backup')
        if not backup.exists():
            backup.write_bytes(index.read_bytes())
        index.write_text(content)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('web_root', type=Path)
    parser.add_argument('--remove', action='store_true')
    args = parser.parse_args()
    update(args.web_root / 'index.html', args.remove)
    print('Removed loader.' if args.remove else 'Installed loader. Reload Jellyfin Web.')
