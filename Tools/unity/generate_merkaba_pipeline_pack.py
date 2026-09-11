#!/usr/bin/env python3
"""Compile captured driver binaries into the native APK payload; never compile SPIR-V.

Inputs are the final source/Unity settings, golden-device .m8pb files, an
independent log of attempted PSO keys, and Unity's graphics-state trace. Coverage
means the exercised capture inventory; device acceptance must exercise every
required app scenario. An observed set alone is not proof of unvisited states.
"""
import argparse
import hashlib
import json
import re
import struct
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
KEY = struct.Struct("<I32s")
HEADER = struct.Struct("<4I")
MAX_BINARY = 64 * 1024 * 1024


def file_hash(path):
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def fingerprint(project, shader_payloads):
    digest = hashlib.sha256()
    files = []
    for folder in ("Runtime", "Editor", "Tools/unity", "Tools/shaders"):
        files.extend((str(p.relative_to(ROOT)), p) for p in (ROOT / folder).rglob("*")
                     if p.is_file() and "__pycache__" not in p.parts)
    files.extend(("Unity/" + str(p.relative_to(project)), p)
                 for folder in ("ProjectSettings", "Packages") for p in (project / folder).rglob("*")
                 if p.is_file() and p.suffix in (".asset", ".json", ".txt"))
    # Freeze the actual scene, URP/XR assets, materials, textures and manifest,
    # not only the settings that happen to be stored in ProjectSettings.
    # Exclude only this generator's outputs, which would hash themselves.
    excluded = {"Plugins/Android/libMerkabaVulkanTimestamps.so",
                "Plugins/Android/libMerkabaVulkanTimestamps.so.meta",
                "StreamingAssets/MerkabaPipelines.meta"}
    for path in (project / "Assets").rglob("*"):
        relative = path.relative_to(project / "Assets").as_posix()
        if (path.is_file() and relative not in excluded and
                not relative.startswith("StreamingAssets/MerkabaPipelines/")):
            files.append(("Unity/Assets/" + relative, path))
    for name, path in sorted(files):
        digest.update(name.encode() + b"\0")
        with path.open("rb") as source:
            for block in iter(lambda: source.read(1024 * 1024), b""):
                digest.update(block)
        digest.update(b"\0")
    digest.update(json.dumps(shader_payloads, sort_keys=True, separators=(",", ":")).encode())
    return digest.digest()


def read_exact(source, size):
    data = source.read(size)
    if len(data) != size:
        raise ValueError("truncated pipeline capture")
    return data


def read_key(source):
    size, data = KEY.unpack(read_exact(source, KEY.size))
    if not 0 < size <= 32 or any(data[size:]):
        raise ValueError("invalid/noncanonical pipeline key")
    return data[:size]


def captures(directory, build):
    pipelines, blobs, source_hashes = {}, {}, {}
    if directory is None:
        return pipelines, blobs, source_hashes
    for path in sorted(directory.rglob("*.m8pb")):
        with path.open("rb") as source:
            magic, version, kind, count = HEADER.unpack(read_exact(source, HEADER.size))
            if magic != 0x4250384D or version != 1 or kind not in (1, 2) or not 0 < count <= 128:
                raise ValueError(f"invalid pipeline capture header: {path}")
            global_key, pipeline_key = read_key(source), read_key(source)
            if read_exact(source, 32) != build:
                raise ValueError(f"capture is not from these final sources/settings: {path}")
            binary_keys = []
            for _ in range(count):
                key = read_key(source)
                size, = struct.unpack("<I", read_exact(source, 4))
                if not 0 < size <= MAX_BINARY:
                    raise ValueError(f"unbounded binary: {path}")
                data = read_exact(source, size)
                identity = global_key, key
                if identity in blobs and blobs[identity] != data:
                    raise ValueError("one driver binary key names different data")
                blobs[identity] = data
                binary_keys.append(key)  # Driver ordering is semantically required.
            if source.read(1):
                raise ValueError(f"trailing pipeline data: {path}")
        identity = global_key, pipeline_key, kind
        if identity in pipelines and pipelines[identity] != binary_keys:
            raise ValueError("conflicting pipeline-to-binaries mapping")
        pipelines[identity] = binary_keys
        source_hashes[str(path)] = file_hash(path)
    return pipelines, blobs, source_hashes


def required_inventory(log, build):
    required, global_key = set(), None
    for line in log.read_text().splitlines():
        device = re.search(r"pipeline-binary mode=CAPTURE globalKey=([0-9a-f]+).*build=([0-9a-f]{64})", line)
        if device:
            if device[2] != build.hex():
                raise ValueError("required-PSO log contains a different source build")
            global_key = bytes.fromhex(device[1])
        item = re.search(r"pipeline required: kind=([12]) key=([0-9a-f]+)", line)
        if item:
            if global_key is None:
                raise ValueError("pipeline request precedes global key in coverage log")
            required.add((global_key, bytes.fromhex(item[2]), int(item[1])))
    if not required or {item[2] for item in required} != {1, 2}:
        raise ValueError("coverage must include native/Unity compute AND graphics PSOs")
    return required


def key_initializer(key):
    return "{" + str(len(key)) + "u,{" + ",".join(str(x) for x in key) + "}}"


def generate(output, mode, build, pipelines, blobs):
    names = {}
    with output.open("w") as code:
        code.write("// Generated from golden-device pipeline binaries. No SPIR-V fallback.\n")
        code.write(f"static constexpr bool kPipelineCaptureMode={'true' if mode == 'CAPTURE' else 'false'};\n")
        code.write(f'static constexpr char kPipelineBuildHex[]="{build.hex()}";\n')
        code.write("static constexpr uint8_t kPipelineBuildId[32]={" + ",".join(str(x) for x in build) + "};\n")
        for index, (identity, data) in enumerate(sorted(blobs.items())):
            names[identity] = f"m8Binary{index}"
            code.write(f"alignas(16) static const uint8_t m8BinaryData{index}[]={{\n")
            for start in range(0, len(data), 24):
                code.write(",".join(str(x) for x in data[start:start + 24]) + ",\n")
            code.write("};\n")
            code.write(f"static const PackedPipelineBinary m8Binary{index}={{" + key_initializer(identity[1]) +
                       f",m8BinaryData{index},{len(data)}u}};\n")
        globals_ = sorted({p[0] for p in pipelines})
        for pack_index, global_key in enumerate(globals_):
            entries = sorted((p for p in pipelines if p[0] == global_key), key=lambda p: (len(p[1]), p[1], p[2]))
            for index, identity in enumerate(entries):
                code.write(f"static const PackedPipelineBinary* const m8Refs{pack_index}_{index}[]={{" +
                           ",".join("&" + names[global_key, key] for key in pipelines[identity]) + "};\n")
            code.write(f"static const PackedPipeline m8Pipelines{pack_index}[]={{\n")
            for index, identity in enumerate(entries):
                code.write("{" + key_initializer(identity[1]) +
                           f",{identity[2]}u,{len(pipelines[identity])}u,m8Refs{pack_index}_{index}}},\n")
            code.write("};\n")
        code.write(f"static constexpr uint32_t kPipelinePackCount={len(globals_)}u;\n")
        code.write(f"static const PipelinePack kPipelinePacks[{max(1,len(globals_))}]={{\n")
        for index, global_key in enumerate(globals_):
            count = sum(p[0] == global_key for p in pipelines)
            code.write("{" + key_initializer(global_key) + f",{count}u,m8Pipelines{index}}},\n")
        if not globals_:
            code.write("{}\n")
        code.write("};\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-project", type=Path)
    parser.add_argument("--mode", choices=("CAPTURE", "BINARY_ONLY"))
    parser.add_argument("--stamp-native", type=Path)
    parser.add_argument("--verify-apk", type=Path)
    parser.add_argument("--verify-source", action="store_true")
    parser.add_argument("--capture-dir", type=Path)
    parser.add_argument("--required-log", type=Path)
    parser.add_argument("--graphics-trace", type=Path)
    parser.add_argument("--shader-metrics", type=Path)
    parser.add_argument("--allow-empty-for-compile", action="store_true")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--manifest", type=Path, required=True)
    args = parser.parse_args()
    if args.verify_source:
        if not args.unity_project:
            parser.error("--verify-source requires --unity-project")
        manifest = json.loads(args.manifest.read_text())
        if manifest["sourceSha256"] != fingerprint(args.unity_project, manifest["shaderPayloads"]).hex():
            raise ValueError("source/prepared Unity content changed after pipeline bundle generation")
        print("Pipeline source/prepared Unity content match PASS")
        return
    if args.stamp_native:
        manifest = json.loads(args.manifest.read_text())
        manifest["nativeSha256"] = file_hash(args.stamp_native)
        args.manifest.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n")
        return
    if args.verify_apk:
        manifest = json.loads(args.manifest.read_text())
        if manifest["compileOnly"] or (manifest["mode"] == "BINARY_ONLY" and manifest["pipelineCount"] == 0):
            raise ValueError("compile-only/empty pipeline payload cannot be installed")
        with zipfile.ZipFile(args.verify_apk) as apk:
            native = apk.read("lib/arm64-v8a/libMerkabaVulkanTimestamps.so")
            if hashlib.sha256(native).hexdigest() != manifest["nativeSha256"]:
                raise ValueError("APK native binary does not match pipeline bundle receipt")
            if native[:4] != b"\x7fELF" or native[4] != 2 or struct.unpack_from("<H", native, 18)[0] != 183:
                raise ValueError("native binary is not ELF64 AArch64")
            if manifest["sourceSha256"].encode() not in native:
                raise ValueError("APK lost the pipeline source fingerprint")
            if json.loads(apk.read("assets/MerkabaPipelines/manifest.json")) != manifest:
                raise ValueError("APK bundle manifest does not match build receipt")
            if any(p.startswith("lib/") and not p.startswith("lib/arm64-v8a/") for p in apk.namelist()):
                raise ValueError("APK must be ARM64 only")
        print("APK native/bundle/source fingerprint verification PASS")
        return
    if not args.unity_project or not args.mode or not args.output or not args.shader_metrics:
        parser.error("generation requires --unity-project, --mode, --output and --shader-metrics")
    shader_payloads = {r["entrypoint"]: r["sha256"] for r in json.loads(args.shader_metrics.read_text())}
    build = fingerprint(args.unity_project, shader_payloads)
    pipelines, blobs, receipts = captures(args.capture_dir, build)
    if args.mode == "CAPTURE" and pipelines:
        raise ValueError("CAPTURE cannot import a release pack")
    if args.mode == "BINARY_ONLY" and not (args.allow_empty_for_compile and not pipelines):
        if not args.required_log or not args.graphics_trace or args.graphics_trace.stat().st_size == 0:
            raise ValueError("binary release requires PSO request log and Unity graphics-state trace")
        required = required_inventory(args.required_log, build)
        if required != set(pipelines):
            raise ValueError(f"PSO coverage mismatch: missing={len(required-set(pipelines))}, extra={len(set(pipelines)-required)}")
    generate(args.output, args.mode, build, pipelines, blobs)
    manifest = {"version": 1, "mode": args.mode, "sourceSha256": build.hex(), "shaderPayloads": shader_payloads,
                "compileOnly": args.allow_empty_for_compile and not pipelines,
                "pipelineCount": len(pipelines), "binaryCount": len(blobs),
                "binaryBytes": sum(map(len, blobs.values())), "captureReceipts": receipts,
                "globalKeys": sorted({p[0].hex() for p in pipelines}),
                "pipelineKeys": sorted(f"{p[0].hex()}:{p[2]}:{p[1].hex()}" for p in pipelines),
                "bundleSha256": file_hash(args.output),
                "graphicsTraceSha256": file_hash(args.graphics_trace) if args.graphics_trace else None,
                "coverageScope": "exercised PSO request inventory; complete app scenarios require device acceptance"}
    args.manifest.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n")
    print(f"Pipeline bundle {args.mode}: source={build.hex()} pipelines={len(pipelines)} binaries={len(blobs)}")


if __name__ == "__main__":
    main()
