"""Install the optional local MARCO worker. Run from the Unity project root."""
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
from urllib.request import urlopen
import venv


def install():
    if not Path('ProjectSettings/ProjectVersion.txt').is_file():
        raise SystemExit('Run setup.py from the Unity project root.')
    root = Path('Library/WhimTex/MARCO').resolve()
    root.mkdir(parents=True, exist_ok=True)
    python = root / ('env/Scripts/python.exe' if os.name == 'nt' else 'env/bin/python')
    if not python.is_file():
        venv.create(root / 'env', with_pip=True)
    subprocess.run([str(python), '-m', 'pip', 'install', 'torch==2.11.0', 'torchvision==0.26.0',
                    'timm==1.0.7', 'safetensors', 'numpy', 'Pillow', 'einops==0.8.0', 'omegaconf==2.3.0'], check=True)
    for name, repo, revision in (
        ('MARCO', 'visinf/MARCO', '1ec16b9382d8619f0cdbb70724b7f031ba174e8e'),
        ('dinov2', 'facebookresearch/dinov2', '7764ea0f912e53c92e82eb78a2a1631e92725fc8'),
    ):
        folder = root / name
        stamp = folder / '.whimtex-revision'
        if stamp.is_file() and stamp.read_text().strip() == revision:
            continue
        if not folder.exists():
            subprocess.run(['git', 'clone', '--no-checkout', 'https://github.com/' + repo, str(folder)], check=True)
        if not (folder / '.git').is_dir():
            raise RuntimeError('Unrecognized local model directory: ' + str(folder))
        subprocess.run(['git', '-C', str(folder), 'checkout', '--detach', revision], check=True)
        stamp.write_text(revision)
    for name, url, digest in (
        ('verified.safetensors', 'https://huggingface.co/timm/vit_large_patch14_reg4_dinov2.lvd142m/resolve/main/model.safetensors',
         'c893d72294d4c327e631ff92f428dbc14c4f93cb5581b6c5f9d89bb5d17def27'),
        ('marco_release.pth', 'https://github.com/visinf/MARCO/releases/download/v1.0/marco_release.pth',
         '4f2254dda18b6f77611155f88cd5e762472b8422ad90d188240de8330d160e99'),
    ):
        target = root / name
        if target.exists() and verified(target, digest):
            continue
        temp = target.with_suffix('.part')
        with urlopen(url, timeout=120) as response, temp.open('wb') as stream:
            shutil.copyfileobj(response, stream)
        if not verified(temp, digest):
            raise RuntimeError('Invalid SHA-256 for ' + name)
        temp.replace(target)


def verified(file, expected):
    with file.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest() == expected


if __name__ == '__main__':
    if sys.version_info < (3, 11):
        raise SystemExit('Python 3.11 or newer is required.')
    install()
