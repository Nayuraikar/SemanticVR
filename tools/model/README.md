---
license: apache-2.0
library_name: onnx
pipeline_tag: image-segmentation
base_model: Roboflow/rf-detr-seg-nano
tags:
  - onnx
  - onnxruntime-web
  - instance-segmentation
  - rf-detr
  - coco
---

# RF-DETR Seg Nano, FP32 ONNX (COCO instance segmentation)

An unchanged copy of an ONNX conversion of Roboflow's **RF-DETR Seg Nano**, kept by
[Imaging for Good](https://huggingface.co/imagingforgood) so that
[browser-perception](https://imagingforgood.github.io/browser-perception/) can rely on
it. We did not train, convert or modify this model.

## Origin

- **Model:** RF-DETR Seg Nano by [Roboflow](https://roboflow.com), Apache-2.0.
  Code: [roboflow/rf-detr](https://github.com/roboflow/rf-detr). Roboflow publishes the
  checkpoint as PyTorch weights (`model.safetensors`), not ONNX:
  [Roboflow/rf-detr-seg-nano](https://huggingface.co/Roboflow/rf-detr-seg-nano).
- **ONNX conversion:** this file, `model.onnx`, is AnnotateIt's (annotateit.ai)
  conversion of that checkpoint. It was published as
  `AnnotateIt/rfdetr-seg-nano-coco-onnx` (commit `dee649fe4daaf940ae186c2164d114fca6e6f0e9`);
  that repository was no longer available on Hugging Face by 2026-10-29, and this copy is
  byte-for-byte the same file (see the SHA-256 below).
- **How it was converted,** according to AnnotateIt's model card: with Roboflow's
  official `rfdetr` exporter, from `roboflow/rf-detr` tag `1.9.4` (commit
  `9b009fa928d6218320439803d1da01869a85c072`). Its evaluation on COCO val2017 (5,000
  images) found the ONNX graph within 0.001 segm AP of the PyTorch checkpoint (segm AP
  0.4010 vs. 0.4011, onnxruntime 1.29 vs. PyTorch 2.13, both on CPU). We have not re-run
  that evaluation; browser-perception checks this file against its own golden outputs.

## File

| File | Bytes | SHA-256 |
|---|---|---|
| `model.onnx` | 122,833,543 | `c03620923d319de71fc756c8585e2585a5ecc6e3d4398cf298c8d2991ceb37c6` |

FP32, opset 17, batch 1, input 312×312, 33.6 M parameters.

## Contract

- Input `input`: `[1, 3, 312, 312]` float32, RGB / 255, then ImageNet mean/std
  (`[0.485, 0.456, 0.406]`, `[0.229, 0.224, 0.225]`), stretch-resized.
- `dets` `[1, 100, 4]`: boxes, normalized cx, cy, w, h.
- `labels` `[1, 100, 91]`: class logits, sparse COCO-91 layout (person = 1).
- `masks` `[1, 100, 78, 78]`: mask logits over the whole (stretched) image.

## Licence

Apache-2.0, as the original model; see `LICENSE`, copied from the
[licence](https://github.com/roboflow/rf-detr/blob/develop/LICENSE) in roboflow/rf-detr.
