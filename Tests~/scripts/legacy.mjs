// Read-only frozen archive source. The working-copy Legacy directory is never consulted.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const packageRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const commit = 'ca8603c0961ce36064280f952259f8a6142d46cc';
const manifestHash = 'c70816ea4220d9ebe08d4d2d087d7e5297bd943405bb3297e24f4b69756a6194';
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const stores = new Map();
const freeze = value => {
    if (value && typeof value === 'object') { Object.values(value).forEach(freeze); Object.freeze(value); }
    return value;
};

export function archiveMetadata(root = packageRoot) {
    const bytes = fs.readFileSync(path.join(root, 'Tests~/legacy-manifest.json'));
    if (digest(bytes) !== manifestHash) throw Error('Frozen archive manifest changed; never regenerate it.');
    const descriptor = JSON.parse(fs.readFileSync(path.join(root, 'Tests~/archive-descriptor.json')));
    if (descriptor.version !== 1 || descriptor.commit !== commit || descriptor.path !== 'Tests~/Legacy' ||
        descriptor.manifest !== 'Tests~/legacy-manifest.json' || descriptor.manifestSha256 !== manifestHash || descriptor.files !== 466)
        throw Error('Pinned archive descriptor changed.');
    const manifest = JSON.parse(bytes);
    if (manifest.files.length !== descriptor.files) throw Error('Archive inventory size differs.');
    return freeze({ descriptor, manifest, manifestHash });
}

function git(root, args, input) {
    const reply = spawnSync('git', args, { cwd: root, input, windowsHide: true,
        timeout: 30000, maxBuffer: 64 * 1024 * 1024 });
    if (reply.error || reply.status !== 0) throw Error('Pinned archive Git read failed: ' +
        (reply.error?.message ?? reply.stderr?.toString('utf8') ?? reply.status));
    return reply.stdout;
}

// Strict byte parser also exercised on malformed synthetic replies by archive tests.
export function decodeGitBlobs(bytes, objects) {
    let cursor = 0;
    const result = [];
    for (const object of objects) {
        const end = bytes.indexOf(10, cursor);
        if (end < 0) throw Error('Truncated Git blob header.');
        const header = bytes.subarray(cursor, end).toString('ascii').split(' ');
        if (header.length !== 3 || header[0] !== object || header[1] !== 'blob' || !/^(0|[1-9][0-9]*)$/.test(header[2]))
            throw Error('Unexpected Git blob header.');
        const length = Number(header[2]);
        cursor = end + 1;
        if (!Number.isSafeInteger(length) || cursor + length >= bytes.length || bytes[cursor + length] !== 10)
            throw Error('Truncated or malformed Git blob body.');
        result.push(Buffer.from(bytes.subarray(cursor, cursor + length)));
        cursor += length + 1;
    }
    if (cursor !== bytes.length) throw Error('Unexpected trailing Git batch data.');
    return result;
}

export function openLegacyArchive(root = packageRoot) {
    root = path.resolve(root);
    const metadata = archiveMetadata(root); // Validate the current descriptor/manifest even on cache hits.
    if (stores.has(root)) return stores.get(root);
    const repository = git(root, ['rev-parse', '--show-toplevel']).toString('utf8').trim();
    if (path.resolve(repository) !== root) throw Error('Archive reader requires this package Git repository.');
    const tree = git(root, ['ls-tree', '-rz', '--full-tree', commit, '--', metadata.descriptor.path])
        .toString('utf8').split('\0').filter(Boolean).map(record => {
            const match = /^(100644|100755) blob ([a-f0-9]{40})\t(.+)$/.exec(record);
            if (!match) throw Error('Archive tree contains a non-regular entry.');
            const file = match[3].slice(metadata.descriptor.path.length + 1);
            if (!match[3].startsWith(metadata.descriptor.path + '/') || !file || file.split('/').some(p => p === '..' || p === '.' || !p))
                throw Error('Unsafe archive tree path.');
            return { file, object: match[2] };
        });
    const expected = [...metadata.manifest.files].sort((a, b) => a.file.localeCompare(b.file));
    tree.sort((a, b) => a.file.localeCompare(b.file));
    if (JSON.stringify(tree.map(f => f.file)) !== JSON.stringify(expected.map(f => f.file))) throw Error('Pinned Git archive inventory differs from the frozen manifest.');
    const blobs = decodeGitBlobs(git(root, ['cat-file', '--batch'], tree.map(f => f.object).join('\n') + '\n'), tree.map(f => f.object));
    const files = new Map();
    tree.forEach((item, index) => {
        const bytes = blobs[index], entry = expected[index];
        if (bytes.length !== entry.bytes || digest(bytes) !== entry.sha256) throw Error('Pinned Git archive bytes differ: ' + item.file);
        files.set(item.file, bytes);
    });
    const directoryNames = new Set(['']);
    for (const file of files.keys()) for (let directory = path.posix.dirname(file); directory !== '.'; directory = path.posix.dirname(directory)) directoryNames.add(directory);
    const store = Object.freeze({
        ...metadata,
        summary: Object.freeze({ files: files.size, baselineCommit: metadata.manifest.baselineCommit,
            manifestHash, source: 'pinned-git', recoveryCommit: commit }),
        read(file, encoding) {
            if (!files.has(file)) throw Error('File is not in the frozen archive: ' + file);
            const bytes = Buffer.from(files.get(file));
            const selected = typeof encoding === 'string' ? encoding : encoding?.encoding;
            return selected ? bytes.toString(selected) : bytes;
        },
        kind(file) { return files.has(file) ? 'file' : directoryNames.has(file) ? 'directory' : null; },
        list(directory) {
            if (!directoryNames.has(directory)) throw Error('Unknown archive directory: ' + directory);
            const prefix = directory ? directory + '/' : '';
            return [...new Set([...files.keys()].filter(f => f.startsWith(prefix)).map(f => f.slice(prefix.length).split('/')[0]))].sort();
        }
    });
    stores.set(root, store);
    return store;
}

export function readLegacy(file, root = packageRoot) { return openLegacyArchive(root).read(file); }
export function verifyLegacy(root = packageRoot) { return { ...openLegacyArchive(root).summary }; }

// Audit tools retain their ordinary fs APIs; ONLY archive paths route to the pinned source.
export function legacyIO(root = packageRoot, baseIO = fs) {
    const archive = openLegacyArchive(root), directory = path.join(path.resolve(root), 'Tests~/Legacy');
    const relative = file => {
        if (file instanceof URL) file = fileURLToPath(file);
        if (typeof file !== 'string') return null;
        const value = path.relative(directory, path.resolve(file));
        return value === '' || !value.startsWith('..' + path.sep) && value !== '..' && !path.isAbsolute(value)
            ? value.replaceAll('\\', '/') : null;
    };
    const stat = file => {
        const kind = archive.kind(file);
        if (!kind) throw Error('Unknown archive path: ' + file);
        return { size: kind === 'file' ? archive.read(file).length : 0,
            isFile: () => kind === 'file', isDirectory: () => kind === 'directory', isSymbolicLink: () => false };
    };
    return {
        ...baseIO,
        readFileSync(file, encoding) { const key = relative(file); return key === null ? baseIO.readFileSync(file, encoding) : archive.read(key, encoding); },
        existsSync(file) { const key = relative(file); return key === null ? baseIO.existsSync(file) : archive.kind(key) !== null; },
        statSync(file) { const key = relative(file); return key === null ? baseIO.statSync(file) : stat(key); },
        lstatSync(file) { const key = relative(file); return key === null ? baseIO.lstatSync(file) : stat(key); },
        readdirSync(file, options) {
            const key = relative(file);
            if (key === null) return baseIO.readdirSync(file, options);
            const names = archive.list(key);
            return options?.withFileTypes ? names.map(name => ({ name, ...stat(key ? key + '/' + name : name) })) : names;
        }
    };
}

if (path.resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) console.log(JSON.stringify(verifyLegacy(), null, 2));
