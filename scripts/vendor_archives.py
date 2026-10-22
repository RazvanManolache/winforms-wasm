"""Extract pinned vendor containers as data; never launch their installers."""
import argparse
import base64
import hashlib
import io
import lzma
from pathlib import Path, PureWindowsPath
import shutil
import struct
import subprocess
import tempfile

MAX_BYTES = 256 * 1024 * 1024


def safe_name(name):
    normalized = name.replace('\\', '/')
    parts = normalized.split('/')
    if (not normalized or PureWindowsPath(normalized).drive or normalized.startswith('/')
            or any(p in ('', '.', '..') or ':' in p or p.rstrip(' .') != p for p in parts)):
        raise ValueError('Unsafe archive path')
    return '/'.join(parts)


def redgate_files(data):
    """Read the version-2, LZMA container used by the pinned Reflector 11.1 installer."""
    stream = io.BytesIO(data)

    def read(fmt):
        size = struct.calcsize(fmt)
        value = stream.read(size)
        if len(value) != size:
            raise ValueError('Truncated container')
        return struct.unpack(fmt, value)[0]

    def string():
        length = 0
        for shift in range(0, 35, 7):
            value = read('<B')
            length |= (value & 127) << shift
            if not value & 128:
                if length > 32768:
                    raise ValueError('Oversized container string')
                raw = stream.read(length)
                if len(raw) != length:
                    raise ValueError('Truncated container string')
                return raw.decode('utf-8')
        raise ValueError('Invalid string length')

    if read('<H') != 60636 or read('<h') != 2:
        raise ValueError('Unsupported container header')
    count = read('<h')
    if not 0 < count <= 10000 or read('<I') != 2:
        raise ValueError('Unsupported container compression or file count')
    if read('<I') != 0 or read('<I') != 0:
        raise ValueError('Unsupported container flags')
    records = []
    names = set()
    for _ in range(count):
        if read('<H') != 60637 or read('<i') != 3:
            raise ValueError('Unsupported file record')
        origin, product = string(), string()
        length, flags, original = read('<i'), read('<I'), read('<i')
        name, offset = safe_name(string()), read('<q')
        if name.lower().endswith(('.exe', '.dll')):
            for _ in range(4):
                read('<i')
        checksum = string()
        if flags not in (0, 1, 2) or min(length, original, offset) < 0 or length != original:
            raise ValueError('Invalid file record range or flags')
        if name.casefold() in names:
            raise ValueError('Duplicate archive path')
        names.add(name.casefold())
        records.append((origin, name, offset, length, flags, checksum))
    blocks = {}
    total = 0
    block_count = read('<i')
    if not 0 < block_count <= 1000:
        raise ValueError('Invalid stream count')
    for _ in range(block_count):
        origin, length = string(), read('<q')
        if origin in blocks or not 13 <= length <= MAX_BYTES:
            raise ValueError('Invalid compressed stream')
        compressed = stream.read(length)
        if len(compressed) != length:
            raise ValueError('Truncated compressed stream')
        expected = struct.unpack('<Q', compressed[5:13])[0]
        total += expected
        if total > MAX_BYTES:
            raise ValueError('Uncompressed container exceeds limit')
        properties = compressed[0]
        lc, remainder = properties % 9, properties // 9
        lp, pb = remainder % 5, remainder // 5
        dictionary = struct.unpack('<I', compressed[1:5])[0]
        if lc + lp > 4 or pb > 4 or not 4096 <= dictionary <= MAX_BYTES:
            raise ValueError('Unsupported LZMA properties')
        try:
            decoded = lzma.LZMADecompressor(format=lzma.FORMAT_RAW, filters=[{
                'id': lzma.FILTER_LZMA1, 'dict_size': dictionary, 'lc': lc, 'lp': lp, 'pb': pb
            }]).decompress(compressed[13:], max_length=expected)
        except lzma.LZMAError as error:
            raise ValueError('Corrupt compressed stream') from error
        if len(decoded) != expected:
            raise ValueError('Uncompressed stream length mismatch')
        blocks[origin] = decoded
    if stream.read(1):
        raise ValueError('Unexpected container trailer')
    files = {}
    for origin, name, offset, length, flags, checksum in records:
        if flags & 1:
            continue
        block = blocks.get(origin)
        if block is None or offset + length > len(block):
            raise ValueError('File extends beyond its stream')
        content = block[offset:offset + length]
        # The installer records MD5 per file. SHA-256 authenticates the whole
        # downloaded package in the preparation script before this parser runs.
        actual = base64.b64encode(hashlib.md5(content, usedforsecurity=False).digest()).decode()
        if actual != checksum:
            raise ValueError('File checksum mismatch')
        files[name] = content
    return files


def extract(kind, installer, destination, sevenzip):
    destination = Path(destination).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='wf-vendor-') as scratch:
        scratch = Path(scratch)

        def unpack(source, output):
            result = subprocess.run([str(sevenzip), 'x', str(source), '-o' + str(output), '-y'],
                                    capture_output=True, text=True)
            if result.returncode:
                raise ValueError('Container extraction failed: ' + result.stderr[-500:])

        unpack(Path(installer).resolve(), scratch / 'outer')
        if kind == 'nested-nsis-fiddler':
            unpack(scratch / 'outer/$PLUGINSDIR/FiddlerSetup.exe', scratch / 'payload')
            source = scratch / 'payload'
            if not (source / 'Fiddler.exe').is_file():
                raise ValueError('Fiddler entry absent from official container')
            shutil.copytree(source, destination, dirs_exist_ok=True)
        elif kind == 'redgate-reflector':
            files = redgate_files((scratch / 'outer/.rsrc/0/TOOLKIT/102').read_bytes())
            prefix = '.NET Reflector Desktop/Red Gate/.NET Reflector/Desktop 11.1/'
            selected = {name[len(prefix):]: content for name, content in files.items()
                        if name.startswith(prefix)}
            if 'Reflector.exe' not in selected:
                raise ValueError('Reflector desktop entry absent from official container')
            for name, content in selected.items():
                target = destination / safe_name(name)
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(content)
        else:
            raise ValueError('Unsupported vendor container')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--kind', choices=['nested-nsis-fiddler', 'redgate-reflector'], required=True)
    parser.add_argument('--installer', required=True)
    parser.add_argument('--destination', required=True)
    parser.add_argument('--sevenzip', required=True)
    args = parser.parse_args()
    extract(args.kind, args.installer, args.destination, args.sevenzip)
