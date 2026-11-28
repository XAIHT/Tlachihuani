# HandGI engine

The engine behind [HandGI](../../docs/handgi.md). It has no window of its own; the
HandGI program ([Tlachihuani.HandGI](../Tlachihuani.HandGI/README.md)) drives it.

- **Capture**: the camera ([CameraCapture.cs](Capture/CameraCapture.cs)).
- **Vision**: the palm detector, the 21 hand points and hand tracking, run as ONNX
  Runtime graphs.
- **Gestures**: the ready-made MediaPipe signs, the signs a user teaches and the
  decision that turns what the camera sees into an action.
- **Audio**: the sound volume and mute ([VolumeController.cs](Audio/VolumeController.cs)).
- **Gpu**: ONNX Runtime on the chosen graphics card through DirectML
  ([OrtFactory.cs](Gpu/OrtFactory.cs)). Without a CPU fallback the runtime refuses to
  run any part of a model on the processor, so "GPU only" is enforced, not just
  promised. [GpuCatalog.cs](Gpu/GpuCatalog.cs) names the supported cards: NVIDIA
  GTX 1060 or newer (DirectX 12), with no CUDA installation. The opt-in CPU
  compatibility mode is slower, uses at most four threads and always leaves one
  processor core for the window.
- **Engine**: the frame loop, the settings and the pinned models
  ([ModelCatalog.cs](Engine/ModelCatalog.cs)). The files, their licenses and their
  hashes are listed in [assets/handgi](../../assets/handgi/README.md).
- **Onnx** and **Training**: reading ONNX and TensorFlow Lite models, and training the
  signs a user teaches on the GPU.
- **SelfTest**: `Tlachihuani.HandGI.exe selftest`.
- **Util**: HandGI's per-user folder, its log and its clock.

Code that runs on every camera frame and in tight GPU or training loops is marked
`tlachihuani:trace-off`; its own messages still reach Tlachihuani.log through
HandGI's log bridge.
