"""Reference for HandGI's port of MediaPipe's gesture embedder + canned classifier.

Usage:  python -I gesture_reference.py <hand_gesture_recognizer dir> <vectors.json out>

1. Reads both .tflite files and extracts the weights by walking the operator list.
2. Re-implements the networks in plain numpy (the exact structure HandGI will emit
   as ONNX) and compares them with TensorFlow Lite itself.
3. Re-implements MediaPipe's landmark preprocessing (pixels -> object-normalised).
4. Builds synthetic hands for the seven gestures and checks what Google's model says.
5. Writes test vectors (computed by TensorFlow Lite) for HandGI's C# self-test.
"""
import json
import math
import os
import sys

import numpy as np
import tensorflow as tf
from tensorflow.lite.python import schema_py_generated as schema_fb
from tensorflow.lite.tools import flatbuffer_utils

LABELS = ["None", "Closed_Fist", "Open_Palm", "Pointing_Up", "Thumb_Down", "Thumb_Up", "Victory", "ILoveYou"]
OPS = {v: k for k, v in vars(schema_fb.BuiltinOperator).items() if not k.startswith("_")}


def read(path):
    model = flatbuffer_utils.read_model(path)
    graph = model.subgraphs[0]
    data = []
    for t in graph.tensors:
        buf = model.buffers[t.buffer]
        if buf.data is not None and len(buf.data):
            dtype = {0: np.float32, 2: np.int32}[t.type]
            shape = [int(s) for s in t.shape]
            data.append(np.frombuffer(bytes(bytearray(buf.data)), dtype=dtype).reshape(shape) if shape else
                        np.frombuffer(bytes(bytearray(buf.data)), dtype=dtype)[0])
        else:
            data.append(None)
    ops = []
    for op in graph.operators:
        code = model.operatorCodes[op.opcodeIndex]
        ops.append((OPS[max(code.builtinCode, code.deprecatedBuiltinCode)], [int(i) for i in op.inputs]))
    return data, ops


def embedder_weights(path):
    data, ops = read(path)
    names = [o[0] for o in ops]
    expected = (["GATHER", "MEAN", "SUB", "UNPACK", "REDUCE_MAX", "REDUCE_MAX", "REDUCE_MIN", "SUB",
                 "REDUCE_MIN", "SUB", "MAXIMUM", "RESHAPE", "STRIDED_SLICE", "ADD", "DIV", "RESHAPE",
                 "FULLY_CONNECTED", "FULLY_CONNECTED"]
                + ["FULLY_CONNECTED", "ADD", "MUL", "ADD"] * 6 + ["FULLY_CONNECTED"])
    assert names == expected, names
    w = {
        "gather_idx": data[ops[0][1][1]].astype(np.int64),
        "eps": float(data[ops[13][1][1]]),
        "skip_w": data[ops[16][1][1]], "skip_b": data[ops[16][1][2]],
        "in_w": data[ops[17][1][1]], "in_b": data[ops[17][1][2]],
        "blk_w": [], "blk_b": [], "bn_mul": [], "bn_add": [],
        "out_w": data[ops[42][1][1]], "out_b": data[ops[42][1][2]],
    }
    for k in range(6):
        base = 18 + 4 * k
        w["blk_w"].append(data[ops[base][1][1]])
        w["blk_b"].append(data[ops[base][1][2]])
        w["bn_mul"].append(data[ops[base + 2][1][1]])
        w["bn_add"].append(data[ops[base + 3][1][1]])
    print("gather indices", w["gather_idx"].tolist(), " eps", repr(w["eps"]))
    print("reduce axis", data[ops[4][1][1]], " mean axis", data[ops[1][1][1]],
          " reshape", data[ops[11][1][1]].tolist(), data[ops[15][1][1]].tolist(),
          " slice", data[ops[12][1][1]].tolist(), data[ops[12][1][3]].tolist())
    return w


def classifier_weights(path):
    data, ops = read(path)
    assert [o[0] for o in ops] == ["MUL", "ADD", "FULLY_CONNECTED", "SOFTMAX"], ops
    return {"bn_mul": data[ops[0][1][1]], "bn_add": data[ops[1][1][1]],
            "w": data[ops[2][1][1]], "b": data[ops[2][1][2]]}


def embed(x, w):
    """x: [N,21,3] object-normalised landmarks -> [N,128]."""
    centre = x[:, w["gather_idx"], :].mean(axis=1, keepdims=True)
    c = x - centre
    rx = c[:, :, 0].max(axis=1) - c[:, :, 0].min(axis=1)
    ry = c[:, :, 1].max(axis=1) - c[:, :, 1].min(axis=1)
    v = (c / (np.maximum(rx, ry)[:, None, None] + w["eps"])).reshape(-1, 63)
    h = v @ w["skip_w"].T + w["skip_b"]
    a = np.maximum(v @ w["in_w"].T + w["in_b"], 0)
    for k in range(6):
        h = h + a @ w["blk_w"][k].T + w["blk_b"][k]
        a = np.maximum(h * w["bn_mul"][k] + w["bn_add"][k], 0)
    return np.maximum(a @ w["out_w"].T + w["out_b"], 0)


def classify(e, c):
    z = np.maximum(e * c["bn_mul"] + c["bn_add"], 0) @ c["w"].T + c["b"]
    z = z - z.max(axis=1, keepdims=True)
    p = np.exp(z)
    return p / p.sum(axis=1, keepdims=True)


def whiten(e, c):
    return e * c["bn_mul"] + c["bn_add"]


def mediapipe_normalise(lm_px, width, height):
    """HandGI landmarks (x, y in image pixels, z in the same pixel units) -> MediaPipe's
    object-normalised embedder input, exactly as MediaPipe's graph prepares it."""
    m = float(max(width, height))
    x = lm_px[:, 0] / width
    y = lm_px[:, 1] / height
    z = lm_px[:, 2] / width / 0.4                       # TensorsToLandmarks normalize_z, then z * rect.width
    x = (x - 0.5) * width / m + 0.5                     # LandmarksToMatrix aspect-ratio normalisation
    y = (y - 0.5) * height / m + 0.5
    p = np.stack([x, y, z], axis=1)
    p = p - p[0]                                         # origin: the wrist
    s = max(np.ptp(p[:, 0]), np.ptp(p[:, 1])) + 1e-5
    return (p / s).astype(np.float32)


# ------------------------------------------------------------------ synthetic hands

BASES = {
    1: (0.22, 0.18, 0.05),
    5: (0.20, 0.95, 0.0), 9: (0.0, 1.0, 0.0), 13: (-0.18, 0.93, 0.0), 17: (-0.34, 0.82, 0.0),
}
LENGTHS = {1: (0.33, 0.27, 0.22), 5: (0.38, 0.22, 0.18), 9: (0.42, 0.26, 0.19), 13: (0.39, 0.24, 0.18), 17: (0.30, 0.18, 0.16)}
STRAIGHT = (4, 4, 3)
CURLED = (88, 100, 62)
HALF = (40, 45, 25)


def finger(base, lengths, flex, splay):
    pts = []
    p = np.array(base, dtype=np.float64)
    phi = 0.0
    cs, sn = math.cos(splay), math.sin(splay)
    for length, f in zip(lengths, flex):
        phi += math.radians(f)
        d = np.array([math.cos(phi) * sn, math.cos(phi) * cs, math.sin(phi)])
        p = p + length * d
        pts.append(p.copy())
    return pts


def thumb(alpha, flex):
    a = math.radians(alpha)
    e1 = np.array([math.sin(a), math.cos(a), 0.0])
    e2 = np.array([-0.6, 0.0, 0.8])
    e2 = e2 - e1 * (e2 @ e1)
    e2 /= np.linalg.norm(e2)
    pts = []
    p = np.array(BASES[1], dtype=np.float64)
    phi = 0.0
    for length, f in zip(LENGTHS[1], flex):
        phi += math.radians(f)
        p = p + length * (math.cos(phi) * e1 + math.sin(phi) * e2)
        pts.append(p.copy())
    return pts


def pose(fingers, thumb_alpha, thumb_flex, splays=(0.10, 0.0, -0.08, -0.18)):
    pts = [np.zeros(3), np.array(BASES[1])]
    pts += thumb(thumb_alpha, thumb_flex)
    for (base, flex, splay) in zip((5, 9, 13, 17), fingers, splays):
        pts.append(np.array(BASES[base]))
        pts += finger(BASES[base], LENGTHS[base], flex, splay)
    # order: 0 wrist, 1 CMC, 2-4 thumb, 5 index MCP, 6-8, 9 middle MCP, ...
    return np.array(pts)


TUCKED = (18, (38, 55, 40))
OUT = (85, (0, 4, 4))
NARROW = (0.10, 0.0, -0.08, -0.18)
# name: (finger flexions, thumb abduction, thumb flexions, splays, roll, yaw)
GESTURES = {
    "Open_Palm": ((STRAIGHT,) * 4, 55, (0, 4, 4), (0.20, 0.03, -0.12, -0.28), 0, 0),
    "Closed_Fist": ((CURLED,) * 4, *TUCKED, NARROW, 0, 0),
    "Pointing_Up": ((STRAIGHT, CURLED, CURLED, CURLED), *TUCKED, (0.05, 0.0, -0.08, -0.18), 0, 0),
    "Victory": ((STRAIGHT, STRAIGHT, CURLED, CURLED), *TUCKED, (0.26, -0.10, -0.08, -0.18), 0, 0),
    "ILoveYou": ((STRAIGHT, CURLED, CURLED, STRAIGHT), 72, (0, 4, 4), (0.15, 0.0, -0.08, -0.30), 0, 0),
    "Thumb_Up": ((CURLED,) * 4, *OUT, NARROW, 90, 0),
    "Thumb_Up side": ((CURLED,) * 4, *OUT, NARROW, 90, 70),
    "Thumb_Down": ((CURLED,) * 4, *OUT, NARROW, -90, 0),
    "Thumb_Down side": ((CURLED,) * 4, *OUT, NARROW, -90, 70),
    "Relaxed (none)": ((HALF,) * 4, 35, (10, 20, 15), NARROW, 0, 0),
    "Pinky up (custom)": ((CURLED, CURLED, CURLED, STRAIGHT), *TUCKED, (0.10, 0.0, -0.08, -0.20), 0, 0),
    "Three fingers (custom)": ((STRAIGHT, STRAIGHT, STRAIGHT, CURLED), *TUCKED, (0.18, 0.0, -0.14, -0.18), 0, 0),
}


def rotation(roll, yaw, pitch):
    r, y, p = (math.radians(v) for v in (roll, yaw, pitch))
    rz = np.array([[math.cos(r), -math.sin(r), 0], [math.sin(r), math.cos(r), 0], [0, 0, 1]])
    ry = np.array([[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]])
    rx = np.array([[1, 0, 0], [0, math.cos(p), -math.sin(p)], [0, math.sin(p), math.cos(p)]])
    return rz @ ry @ rx


def render(name, rng, width=1280, height=720, jitter=True):
    fingers, alpha, tflex, splays, roll, yaw = GESTURES[name]
    pitch = 0.0
    if jitter:
        fingers = tuple(tuple(f + rng.normal(0, 5) for f in fl) for fl in fingers)
        alpha += rng.normal(0, 6)
        tflex = tuple(f + rng.normal(0, 5) for f in tflex)
        roll += rng.normal(0, 10)
        yaw, pitch = yaw + rng.normal(0, 15), rng.normal(0, 10)
        scale = rng.uniform(110, 200)
        cx, cy = rng.uniform(0.3, 0.7) * width, rng.uniform(0.45, 0.75) * height
    else:
        scale, cx, cy = 160.0, width * 0.5, height * 0.62
    p = pose(fingers, alpha, tflex, splays) @ rotation(roll, yaw, pitch).T
    px = np.stack([cx + scale * p[:, 0], cy - scale * p[:, 1], -scale * p[:, 2]], axis=1)
    if jitter:
        px[:, :2] += rng.normal(0, 1.2, size=(21, 2))
        px[:, 2] += rng.normal(0, 2.0, size=21)
    return px.astype(np.float32)


def main():
    root, out = sys.argv[1], sys.argv[2]
    emb_path = os.path.join(root, "gesture_embedder.tflite")
    clf_path = os.path.join(root, "canned_gesture_classifier.tflite")
    w = embedder_weights(emb_path)
    c = classifier_weights(clf_path)

    emb_tfl = tf.lite.Interpreter(model_path=emb_path)
    emb_tfl.allocate_tensors()
    clf_tfl = tf.lite.Interpreter(model_path=clf_path)
    clf_tfl.allocate_tensors()
    ein = {d["name"]: d["index"] for d in emb_tfl.get_input_details()}
    eout = emb_tfl.get_output_details()[0]["index"]
    cin = clf_tfl.get_input_details()[0]["index"]
    cout = clf_tfl.get_output_details()[0]["index"]

    def tflite(x):
        emb_tfl.set_tensor(ein["hand"], x.reshape(1, 21, 3).astype(np.float32))
        emb_tfl.set_tensor(ein["handedness"], np.array([[0.7]], np.float32))
        emb_tfl.set_tensor(ein["world_hand"], np.zeros((1, 21, 3), np.float32))
        emb_tfl.invoke()
        e = emb_tfl.get_tensor(eout).copy()
        clf_tfl.set_tensor(cin, e)
        clf_tfl.invoke()
        return e[0], clf_tfl.get_tensor(cout)[0].copy()

    rng = np.random.default_rng(20261126)
    # 1. structure check on random inputs and on the synthetic hands
    worst_e = worst_p = 0.0
    for i in range(200):
        x = rng.normal(0, 0.5, size=(21, 3)).astype(np.float32)
        e_ref, p_ref = tflite(x)
        e_np = embed(x[None], w)[0]
        p_np = classify(e_np[None], c)[0]
        worst_e = max(worst_e, float(np.abs(e_np - e_ref).max() / (np.abs(e_ref).max() + 1e-6)))
        worst_p = max(worst_p, float(np.abs(p_np - p_ref).max()))
    print(f"numpy vs TFLite on 200 random inputs: embedding rel err {worst_e:.2e}, probability err {worst_p:.2e}")

    # 2. handedness / world landmarks really are unused
    x = rng.normal(0, 0.5, size=(21, 3)).astype(np.float32)
    emb_tfl.set_tensor(ein["hand"], x.reshape(1, 21, 3))
    emb_tfl.set_tensor(ein["handedness"], np.array([[0.0]], np.float32))
    emb_tfl.set_tensor(ein["world_hand"], rng.normal(0, 1, size=(1, 21, 3)).astype(np.float32))
    emb_tfl.invoke()
    a = emb_tfl.get_tensor(eout).copy()
    emb_tfl.set_tensor(ein["handedness"], np.array([[1.0]], np.float32))
    emb_tfl.set_tensor(ein["world_hand"], rng.normal(0, 1, size=(1, 21, 3)).astype(np.float32))
    emb_tfl.invoke()
    b = emb_tfl.get_tensor(eout).copy()
    print(f"handedness + world landmarks change the embedding by {float(np.abs(a - b).max()):.2e} (0 = unused)")

    # 3. synthetic gestures
    print("\nSynthetic hands (40 random variations each): what Google's model answers")
    stats = {}
    for name in GESTURES:
        votes = np.zeros(8)
        conf = []
        embs = []
        for i in range(40):
            px = render(name, rng)
            x = mediapipe_normalise(px, 1280, 720)
            e, p = tflite(x)
            votes[int(p.argmax())] += 1
            conf.append(float(p.max()))
            embs.append(whiten(e[None], c)[0])
        top = int(votes.argmax())
        stats[name] = np.array(embs)
        share = ", ".join(f"{LABELS[k]} {int(v)}" for k, v in enumerate(votes) if v)
        print(f"  {name:24s} -> {LABELS[top]:12s} ({share}); mean top score {np.mean(conf):.2f}")

    # 4. how separable are the gestures for 'your own gesture' matching?
    def unit(v):
        return v / (np.linalg.norm(v, axis=-1, keepdims=True) + 1e-9)
    print("\nCosine similarity to each gesture's centroid (whitened embedding)")
    cents = {n: unit(unit(v).mean(axis=0)) for n, v in stats.items()}
    for n, v in stats.items():
        own = unit(v) @ cents[n]
        other = max(float((unit(v) @ cents[m]).max()) for m in stats if m != n)
        print(f"  {n:24s} own: min {own.min():.3f} p05 {np.percentile(own, 5):.3f} median {np.median(own):.3f} | best other {other:.3f}")

    # 5. test vectors for the C# self-test
    cases = []
    sizes = [(1280, 720), (640, 480), (720, 1280)]
    for i, name in enumerate(list(GESTURES)):
        for j, (wd, ht) in enumerate(sizes if i < 3 else sizes[:1]):
            px = render(name, rng, wd, ht, jitter=(j > 0 or i % 2 == 1))
            x = mediapipe_normalise(px, wd, ht)
            e, p = tflite(x)
            cases.append({"name": f"{name} {wd}x{ht}", "width": wd, "height": ht,
                          "landmarks_px": [round(float(v), 4) for v in px.ravel()],
                          "normalized": [float(v) for v in x.ravel()],
                          "embedding": [float(v) for v in e],
                          "probs": [float(v) for v in p]})
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"source": "MediaPipe gesture_recognizer.task (float16/latest, MD5 4dbf485c473207651100a1895343f35a), outputs computed by TensorFlow Lite",
                   "labels": LABELS, "cases": cases}, f, indent=1)
    print(f"\nwrote {len(cases)} test vectors to {out}")
    print("REFERENCE DONE")


if __name__ == "__main__":
    main()
