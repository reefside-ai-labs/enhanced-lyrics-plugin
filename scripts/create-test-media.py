#!/usr/bin/env python3
"""Create original synthetic audio and lyric fixtures for the isolated Docker test."""
import math
import shutil
import struct
import wave
from pathlib import Path
root = Path(__file__).resolve().parent.parent
media_root = root / '.test-data/media'
media = media_root / 'Test Artist' / 'Synthetic Album'
media.mkdir(parents=True, exist_ok=True)
for title in ('Synthetic', 'Enhanced LRC', 'Plain', 'Fallback', 'No lyrics', 'Visual Features'):
    with wave.open(str(media / (title + '.wav')), 'wb') as audio:
        audio.setparams((1, 2, 16000, 0, 'NONE', 'not compressed'))
        for second in range(32 if title == 'Visual Features' else 24):
            audio.writeframes(b''.join(struct.pack('<h', int(1000 * math.sin(2 * math.pi * 220 * (second * 16000 + i) / 16000))) for i in range(16000)))
shutil.copy(root / 'tests/fixtures/Synthetic.ttml', media / 'Synthetic.ttml')
shutil.copy(root / 'tests/fixtures/Visual Features.ttml', media / 'Visual Features.ttml')
shutil.copy(root / 'tests/fixtures/Synthetic.elrc', media / 'Enhanced LRC.elrc')
(media / 'Plain.txt').write_text('Original synthetic lyrics\nA second line\nA third line\n')
(media / 'Fallback.ttml').write_text('<broken>')
shutil.copy(root / 'tests/fixtures/Synthetic.elrc', media / 'Fallback.elrc')

# Original test artwork: a small RGB gradient, encoded as PNG without dependencies.
import zlib
width = 128
pixels = b''.join(b'\x00' + b''.join(bytes((int(220*x/width), int(160*y/width), int(240*(1-x/width)))) for x in range(width)) for y in range(width))
def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
(media / 'cover.png').write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, width, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(pixels)) + chunk(b'IEND', b''))

# Remove only the flat fixtures created by earlier versions of this test script.
for name in ('Synthetic.wav', 'Enhanced LRC.wav', 'Plain.wav', 'Fallback.wav', 'No lyrics.wav', 'Synthetic.ttml', 'Enhanced LRC.elrc', 'Plain.txt', 'Fallback.ttml', 'Fallback.elrc', 'cover.png'):
    previous = media_root / name
    if previous.exists():
        previous.unlink()
