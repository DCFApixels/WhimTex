"""Generate local MARCO point pairs for two rendered WhimTex keys."""
import json
import os
from pathlib import Path
import sys


def match(runtime, job):
    def check_pending():
        if not (job / 'pending').exists():
            raise SystemExit(0)

    check_pending()
    sys.path[:0] = [str(runtime / 'MARCO'), str(runtime / 'dinov2')]
    import numpy as np
    import torch
    from PIL import Image
    from omegaconf import OmegaConf
    from safetensors.torch import load_file
    from dinov2.hub.backbones import dinov2_vitl14_reg
    from models.adapters import inject_adapters
    from models.marco import MARCO

    check_pending()
    torch.set_num_threads(min(8, os.cpu_count() or 1))
    backbone = dinov2_vitl14_reg(pretrained=False)
    weights = load_file(str(runtime / 'verified.safetensors'))
    weights['register_tokens'] = weights.pop('reg_token')
    # This checkpoint folds the CLS position into the token. No tokens are masked at inference.
    weights['pos_embed'] = torch.cat([torch.zeros_like(weights['cls_token']), weights['pos_embed']], 1)
    weights['mask_token'] = torch.zeros_like(backbone.mask_token)
    backbone.load_state_dict(weights)
    del weights
    cfg = OmegaConf.load(runtime / 'MARCO/configs/model/marco.yaml')
    inject_adapters(backbone, range(*cfg.adaptformer_stages), backbone.embed_dim, cfg.channel_factor)
    model = MARCO(backbone, backbone.embed_dim, cfg)
    checkpoint = torch.load(runtime / 'marco_release.pth', map_location='cpu', weights_only=True)
    model.load_state_dict(checkpoint['model'])
    model.eval()

    with torch.inference_mode():
        maps = []
        coverage = []
        for name in ('a.png', 'b.png'):
            check_pending()
            with Image.open(job / name) as image:
                w, h = image.size
                # Keep the aspect ratio and use whole 14-pixel patches.
                size = tuple(max(14, round(37 * dim / max(w, h)) * 14) for dim in (w, h))
                rgba = image.convert('RGBA')
                alpha = rgba.getchannel('A')
                coverage.append(np.asarray(alpha))
                if alpha.getextrema()[1] == 0:
                    return []
                background = Image.new('RGB', rgba.size, (127, 127, 127))
                background.paste(rgba, mask=alpha)
                if all(high == low for low, high in background.getextrema()):
                    return []
                rgb = np.asarray(background.resize(size, Image.Resampling.BILINEAR), dtype=np.float32) / 255
            rgb -= np.array([.485, .456, .406], dtype=np.float32)
            rgb /= np.array([.229, .224, .225], dtype=np.float32)
            tensor = torch.from_numpy(rgb).permute(2, 0, 1)[None].contiguous()
            maps.append(model.upscale(backbone.get_intermediate_layers(tensor, n=1, reshape=True)[0]))

        dims = np.maximum(2, np.rint(np.array([w, h]) * 512 / max(w, h))).astype(int)
        image_size = (int(dims[1]), int(dims[0]))
        def predict(source, target, points):
            features = torch.stack([source, target], dim=1)
            result = []
            for start in range(0, len(points), 32):
                check_pending()
                query = torch.tensor(points[start:start+32], dtype=torch.float32)[None]
                logits = model.sample_descriptors_w_sim(features, query, image_size)
                result.append(model.predict_from_logits(logits, image_size)[0].numpy())
            return np.concatenate(result)

        axes = [np.arange(min(24, (dim-1)//2), dim, 40) for dim in dims]
        source = np.stack(np.meshgrid(*axes), axis=-1).reshape(-1, 2)
        target = predict(*maps, source)
        residual = np.linalg.norm(predict(maps[1], maps[0], target) - source, axis=1)

    def uv(point):
        x, y = (point + .5) / dims
        return {'x': float(x), 'y': float(1 - y)}

    selected = []
    for i in np.argsort(residual):
        if not np.isfinite(residual[i]) or residual[i] > 12 or len(selected) == 40:
            break
        if any(mask[min(h-1, int((xy[1]+.5)*h/dims[1])), min(w-1, int((xy[0]+.5)*w/dims[0]))] < 16
               for xy, mask in zip((source[i], target[i]), coverage)):
            continue
        if any(np.linalg.norm(source[i] - source[j]) < 65 or
               np.linalg.norm(target[i] - target[j]) < 36 for j in selected):
            continue
        selected.append(i)
    return [{'from': uv(source[i]), 'to': uv(target[i]), 'radius': .19} for i in selected]


if __name__ == '__main__':
    directory = Path(sys.argv[2])
    try:
        anchors = match(Path(sys.argv[1]), directory)
        if (directory / 'pending').exists():
            (directory / 'matches.json').write_text(json.dumps({'anchors': anchors}, allow_nan=False), encoding='utf-8')
    except Exception as exc:
        if (directory / 'pending').exists():
            (directory / 'error.txt').write_text(str(exc), encoding='utf-8')
            raise
    finally:
        if directory.is_dir() and not (directory / 'pending').exists():
            for name in ('a.png', 'b.png', 'matches.json', 'error.txt'):
                (directory / name).unlink(missing_ok=True)
            try:
                directory.rmdir()
            except FileNotFoundError:
                pass
