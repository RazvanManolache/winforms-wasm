import base64
import hashlib
import importlib.util
import lzma
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location('vendor_archives',
    Path(__file__).resolve().parents[2] / 'scripts/vendor_archives.py')
archive = importlib.util.module_from_spec(spec)
spec.loader.exec_module(archive)


def string(value):
    value = value.encode()
    length = len(value)
    prefix = bytearray()
    while length >= 128:
        prefix.append((length & 127) | 128)
        length >>= 7
    return bytes(prefix + bytes([length])) + value


def container(name='demo/data.txt', content=b'known contents', *, checksum=None, offset=0, duplicate=False):
    digest = checksum or base64.b64encode(hashlib.md5(content).digest()).decode()
    record = (struct.pack('<Hi', 60637, 3) + string('product') + string('product')
        + struct.pack('<iIi', len(content), 0, len(content)) + string(name)
        + struct.pack('<q', offset) + string(digest))
    compressed = lzma.compress(content, format=lzma.FORMAT_ALONE)
    compressed = compressed[:5] + struct.pack('<Q', len(content)) + compressed[13:]
    return (struct.pack('<HhhIII', 60636, 2, 2 if duplicate else 1, 2, 0, 0)
        + record * (2 if duplicate else 1) + struct.pack('<i', 1) + string('product')
        + struct.pack('<q', len(compressed)) + compressed)


class VendorArchiveTests(unittest.TestCase):
    def test_extracts_exact_bytes_without_executing_payload(self):
        self.assertEqual({'demo/data.txt': b'known contents'}, archive.redgate_files(container()))

    def test_rejects_windows_and_posix_escape_paths(self):
        for path in ['../escape', r'C:\escape', r'\\host\share', '/absolute', 'a/../../b', 'a/.. /b']:
            with self.subTest(path=path), self.assertRaises(ValueError):
                archive.redgate_files(container(name=path))

    def test_rejects_corrupt_file_checksum(self):
        with self.assertRaisesRegex(ValueError, 'checksum'):
            archive.redgate_files(container(checksum='incorrect'))

    def test_rejects_file_outside_decoded_stream(self):
        with self.assertRaisesRegex(ValueError, 'beyond'):
            archive.redgate_files(container(offset=1))

    def test_rejects_truncation_and_unexpected_trailer(self):
        for data in [container()[:-3], container() + b'garbage', b'\xdc\xec']:
            with self.subTest(size=len(data)), self.assertRaises(ValueError):
                archive.redgate_files(data)

    def test_rejects_case_insensitive_path_collisions(self):
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            archive.redgate_files(container(duplicate=True))

    def test_rejects_oversized_decompression_before_allocating(self):
        data = container()
        length = struct.unpack('<q', data[-(len(lzma.compress(b'known contents', format=lzma.FORMAT_ALONE)) + 8):][:8])[0]
        start = len(data) - length
        data = data[:start + 5] + struct.pack('<Q', archive.MAX_BYTES + 1) + data[start + 13:]
        with self.assertRaisesRegex(ValueError, 'limit'):
            archive.redgate_files(data)


if __name__ == '__main__':
    unittest.main()
