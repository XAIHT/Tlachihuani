# HandGI models

The three files in `models/` are the networks HandGI uses to find a hand, follow its
21 points and recognize ready-made signs. They are third-party models under the
Apache License 2.0, shipped unchanged. The build copies them next to
`Tlachihuani.HandGI.exe` (`HandGI\models\` in the installation), and the setup builder
(`packaging/setup/build.py`) refuses to package without all three.

| File | What it is | Bytes | SHA-256 | Source |
|---|---|---|---|---|
| `palm_detection_mediapipe_2023feb.onnx` | Palm detector · MediaPipe, ONNX export from OpenCV Zoo (2023-02) | 3,905,734 | `78FF51C38496B7FC8B8EBDB6CC8C1ABB02FA6C38427C6848254CDABA57FCCE7C` | [huggingface.co/opencv/palm_detection_mediapipe](https://huggingface.co/opencv/palm_detection_mediapipe) |
| `handpose_estimation_mediapipe_2023feb.onnx` | Hand landmarks · MediaPipe, ONNX export from OpenCV Zoo (2023-02) | 4,099,621 | `DB0898AE717B76B075D9BF563AF315B29562E11F8DF5027A1EF07B02BEF6D81C` | [huggingface.co/opencv/handpose_estimation_mediapipe](https://huggingface.co/opencv/handpose_estimation_mediapipe) |
| `gesture_recognizer.task` | Gesture recognizer · Google MediaPipe, float16, version 1 | 8,373,440 | `97952348CF6A6A4915C2EA1496B4B37EBABC50CBBF80571435643C455F2B0482` | [storage.googleapis.com/mediapipe-models](https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/1/gesture_recognizer.task) |

The sizes and hashes are pinned in
[ModelCatalog.cs](../../src/Tlachihuani.HandGI.Core/Engine/ModelCatalog.cs). HandGI
verifies every load against them and refuses a corrupted or swapped file. It reads
the copy next to its executable first, then one downloaded on request into the
user's HandGI folder (`%LOCALAPPDATA%\XAIHT\Tlachihuani\HandGI\models`).

HandGI rebuilds the two small gesture networks inside `gesture_recognizer.task` as
GPU graphs; the file itself is not modified. To replace a model, change the file and
its pinned size and hash together, then run HandGI's self-tests
(`Tlachihuani.HandGI.exe selftest`) and its [visible checks](../../docs/handgi.md#checks).
