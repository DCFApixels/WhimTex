import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const packageRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const walk = directory => fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const file = path.join(directory, entry.name);
    if (entry.isSymbolicLink()) throw Error('Legacy may not contain symlinks: ' + file);
    return entry.isDirectory() ? walk(file) : [file];
});

export function verifyLegacy(root = packageRoot) {
    const manifestSource = fs.readFileSync(path.join(root, 'Tests~/legacy-manifest.json'), 'utf8').replaceAll('\r\n', '\n');
    const manifestHash = hash(manifestSource);
    if (manifestHash !== 'c70816ea4220d9ebe08d4d2d087d7e5297bd943405bb3297e24f4b69756a6194') throw Error('Frozen archive manifest changed; do not regenerate it to hide changes.');
    const manifest = JSON.parse(manifestSource);
    const directory = path.join(root, 'Tests~/Legacy');
    const actual = walk(directory).map(file => path.relative(directory, file).replaceAll('\\', '/')).sort();
    const expected = manifest.files.map(entry => entry.file).sort();
    if (JSON.stringify(actual) !== JSON.stringify(expected)) throw Error('Legacy file inventory changed.');
    for (const entry of manifest.files) {
        const bytes = fs.readFileSync(path.join(directory, entry.file));
        if (bytes.length !== entry.bytes || hash(bytes) !== entry.sha256) throw Error('Legacy bytes changed: ' + entry.file);
    }
    return { files: actual.length, baselineCommit: manifest.baselineCommit,
        manifestHash };
}

// Old Node tests derive paths from import.meta.url. Restore their ORIGINAL layout in Temp,
// with current repository sources. Do not rewrite or import the archive in its relocated layout.
export async function createLegacyMirror(directory, root, runProcess) {
    const archive = verifyLegacy(root);
    const reply = await runProcess('git', ['ls-files', '--cached', '--others', '--exclude-standard', '-z'], { cwd: root, timeoutMs: 10000 });
    if (reply.code !== 0 || reply.timedOut || reply.error) throw Error('Cannot inventory current repository for legacy Node execution.');
    fs.mkdirSync(directory, { recursive: false });
    const copied = new Set();
    const digest = createHash('sha256');
    for (const relative of [...new Set(reply.stdout.split('\0').filter(Boolean))].sort()) {
        if (relative.startsWith('Tests~/')) continue;
        const source = path.join(root, relative);
        if (!fs.existsSync(source)) continue; // Deleted tracked paths before staging.
        if (!fs.lstatSync(source).isFile()) throw Error('Mirror only accepts ordinary source files: ' + relative);
        const target = path.resolve(directory, relative);
        if (!target.startsWith(directory + path.sep)) throw Error('Mirror path escape.');
        fs.mkdirSync(path.dirname(target), { recursive: true });
        const bytes = fs.readFileSync(source); fs.writeFileSync(target, bytes, { flag: 'wx' });
        digest.update(relative).update(bytes); copied.add(relative);
    }
    fs.cpSync(path.join(root, 'Tests~/Legacy'), path.join(directory, 'Tests~'), { recursive: true, errorOnExist: true, force: false });
    return { directory, archive, productionSnapshotHash: digest.digest('hex'), productionFiles: copied.size };
}

if (path.resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) console.log(JSON.stringify(verifyLegacy(), null, 2));
