// Reproducible VSIX packaging with Node built-ins; no marketplace tooling required.
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { deflateRawSync } from 'node:zlib';

const root = new URL('./', import.meta.url);
const files = ['[Content_Types].xml', 'extension.vsixmanifest', 'package.json', 'README.md',
  'extension.js', 'metadata.js', 'syntaxes/whimtex-hlsl.tmLanguage.json', 'snippets/whimtex.json'];

function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
  }
  return (crc ^ 0xffffffff) >>> 0;
}

export function buildVsix() {
  const local = [], central = [];
  let offset = 0;
  for (const file of files) {
    const name = Buffer.from(file.startsWith('[') || file === 'extension.vsixmanifest' ? file : 'extension/' + file);
    // Git's CRLF checkout setting must not change the distributed archive.
    const data = Buffer.from(fs.readFileSync(new URL(file, root), 'utf8').replace(/\r\n/g, '\n'));
    const packed = deflateRawSync(data, { level: 9 }), crc = crc32(data);
    const header = Buffer.alloc(30);
    header.writeUInt32LE(0x04034b50); header.writeUInt16LE(20, 4);
    header.writeUInt16LE(8, 8); header.writeUInt16LE(33, 12); // deflate, 1980-01-01
    header.writeUInt32LE(crc, 14); header.writeUInt32LE(packed.length, 18);
    header.writeUInt32LE(data.length, 22); header.writeUInt16LE(name.length, 26);
    const record = Buffer.alloc(46);
    record.writeUInt32LE(0x02014b50); record.writeUInt16LE(20, 4); record.writeUInt16LE(20, 6);
    record.writeUInt16LE(8, 10); record.writeUInt16LE(33, 14);
    record.writeUInt32LE(crc, 16); record.writeUInt32LE(packed.length, 20);
    record.writeUInt32LE(data.length, 24); record.writeUInt16LE(name.length, 28);
    record.writeUInt32LE(offset, 42);
    local.push(header, name, packed); central.push(record, name);
    offset += header.length + name.length + packed.length;
  }
  const directory = Buffer.concat(central), end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50); end.writeUInt16LE(files.length, 8); end.writeUInt16LE(files.length, 10);
  end.writeUInt32LE(directory.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...local, directory, end]);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const archive = new URL('whimtex-fx-tools.vsix', root), bytes = buildVsix();
  if (process.argv.includes('--check')) {
    if (!fs.readFileSync(archive).equals(bytes)) throw new Error('Bundled VSIX is stale. Run build-vsix.mjs.');
    console.log('Bundled VSIX matches extension sources.');
  } else {
    fs.writeFileSync(archive, bytes);
    console.log(`Built WhimTex VSIX (${bytes.length} bytes).`);
  }
}
