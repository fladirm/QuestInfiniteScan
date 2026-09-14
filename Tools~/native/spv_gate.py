#!/usr/bin/env python3
"""FinalScan kernel envelope gate (contract §15.6, fail-closed).

Scans SPIR-V compute modules and fails when any of them exceeds the envelope:
  --max-bytes             SPIR-V size            (default 131072 = 128 KiB)
  --max-storage-bindings  writable storage-buffer bindings (default 8)
  --max-threads           threads per workgroup  (default 256)
  --max-shared            workgroup (groupshared) bytes (default 16384)
  dynamic vector component indexing (OpVectorInsertDynamic / OpVectorExtractDynamic, or an access
  chain step into a vector with a non-constant index) is always refused (Adreno linker, ledger #21).
Unbounded loops cannot be proven statically; the loop count is reported for review only.

Usage: spv_gate.py [options] file.spv [file.spv ...]     exit 0 = every file passes, 1 = violation,
                                                          2 = malformed module / usage error.
Output: one JSON line per file ("FS-SPV-GATE {...}") plus "FAIL <file>: <reason>" lines.
"""
import argparse, json, os, struct, sys

MAGIC = 0x07230203
# opcodes
OP_EXECUTION_MODE, OP_EXECUTION_MODE_ID = 16, 331
OP_TYPE_BOOL, OP_TYPE_INT, OP_TYPE_FLOAT, OP_TYPE_VECTOR, OP_TYPE_MATRIX = 20, 21, 22, 23, 24
OP_TYPE_ARRAY, OP_TYPE_RUNTIME_ARRAY, OP_TYPE_STRUCT, OP_TYPE_POINTER = 28, 29, 30, 32
OP_CONSTANT, OP_SPEC_CONSTANT = 43, 50
OP_VARIABLE = 59
OP_ACCESS_CHAIN, OP_IN_BOUNDS_ACCESS_CHAIN, OP_PTR_ACCESS_CHAIN, OP_IN_BOUNDS_PTR_ACCESS_CHAIN = 65, 66, 67, 70
OP_DECORATE, OP_MEMBER_DECORATE = 71, 72
OP_VECTOR_EXTRACT_DYNAMIC, OP_VECTOR_INSERT_DYNAMIC = 77, 78
OP_LOOP_MERGE = 246
# execution modes
MODE_LOCAL_SIZE, MODE_LOCAL_SIZE_ID = 17, 38
# storage classes
SC_UNIFORM, SC_WORKGROUP, SC_PUSH_CONSTANT, SC_STORAGE_BUFFER = 2, 4, 9, 12
# decorations
DEC_BLOCK, DEC_BUFFER_BLOCK, DEC_NON_WRITABLE, DEC_BINDING, DEC_DESCRIPTOR_SET = 2, 3, 24, 33, 34


class Module:
    def __init__(self, words):
        self.words = words
        self.types = {}          # id -> (opcode, operands)
        self.constants = {}      # id -> int value (32/64-bit ints only)
        self.const_ids = set()   # every OpConstant*/OpSpecConstant* result id
        self.variables = []      # (result_type, result_id, storage_class)
        self.decorations = {}    # id -> set(decoration)
        self.member_nonwritable = {}  # struct id -> set(member index)
        self.local_size = None
        self.local_size_ids = None
        self.dynamic_vector_ops = 0
        self.dynamic_vector_chains = 0
        self.loops = 0
        self.entry_points = 0
        self.instructions = 0

    def parse(self):
        w = self.words
        i = 5
        n = len(w)
        access_chains = []
        while i < n:
            head = w[i]
            count = head >> 16
            op = head & 0xFFFF
            if count == 0 or i + count > n:
                raise ValueError("malformed instruction at word %d" % i)
            ops = w[i + 1:i + count]
            self.instructions += 1
            if op == 15:
                self.entry_points += 1
            elif op == OP_EXECUTION_MODE and len(ops) >= 2 and ops[1] == MODE_LOCAL_SIZE:
                self.local_size = tuple(ops[2:5])
            elif op == OP_EXECUTION_MODE_ID and len(ops) >= 2 and ops[1] == MODE_LOCAL_SIZE_ID:
                self.local_size_ids = tuple(ops[2:5])
            elif op in (OP_TYPE_BOOL, OP_TYPE_INT, OP_TYPE_FLOAT, OP_TYPE_VECTOR, OP_TYPE_MATRIX, OP_TYPE_ARRAY,
                        OP_TYPE_RUNTIME_ARRAY, OP_TYPE_STRUCT, OP_TYPE_POINTER):
                self.types[ops[0]] = (op, ops[1:])
            elif op in (OP_CONSTANT, OP_SPEC_CONSTANT):
                self.const_ids.add(ops[1])
                if len(ops) >= 3:
                    value = ops[2]
                    if len(ops) >= 4:
                        value |= ops[3] << 32
                    self.constants[ops[1]] = value
            elif 44 <= op <= 52 or op == 41 or op == 42:      # other constants (composite, true/false, null, spec ops)
                if len(ops) >= 2:
                    self.const_ids.add(ops[1])
            elif op == OP_VARIABLE:
                self.variables.append((ops[0], ops[1], ops[2]))
            elif op == OP_DECORATE:
                self.decorations.setdefault(ops[0], set()).add(ops[1])
            elif op == OP_MEMBER_DECORATE:
                if ops[2] == DEC_NON_WRITABLE:
                    self.member_nonwritable.setdefault(ops[0], set()).add(ops[1])
            elif op in (OP_VECTOR_EXTRACT_DYNAMIC, OP_VECTOR_INSERT_DYNAMIC):
                self.dynamic_vector_ops += 1
            elif op in (OP_ACCESS_CHAIN, OP_IN_BOUNDS_ACCESS_CHAIN, OP_PTR_ACCESS_CHAIN, OP_IN_BOUNDS_PTR_ACCESS_CHAIN):
                access_chains.append((op, ops))
            elif op == OP_LOOP_MERGE:
                self.loops += 1
            i += count
        # access chains are resolved after every type is known
        self.pointer_types = {}   # pointer result id -> pointee type id (from OpVariable / chain results)
        for rtype, rid, _sc in self.variables:
            self.pointer_types[rid] = self.pointee(rtype)
        for op, ops in access_chains:
            base = ops[2]
            indices = list(ops[3:])
            if op in (OP_PTR_ACCESS_CHAIN, OP_IN_BOUNDS_PTR_ACCESS_CHAIN) and indices:
                indices = indices[1:]        # element index first, then the normal chain
            cur = self.pointer_types.get(base)
            if cur is None:
                cur = self.pointee(ops[0]) if False else None
            for idx in indices:
                if cur is None:
                    break
                kind, targs = self.types.get(cur, (None, ()))
                if kind == OP_TYPE_VECTOR:
                    if idx not in self.const_ids:
                        self.dynamic_vector_chains += 1
                    cur = targs[0]
                elif kind == OP_TYPE_STRUCT:
                    member = self.constants.get(idx)
                    cur = targs[member] if member is not None and member < len(targs) else None
                elif kind in (OP_TYPE_ARRAY, OP_TYPE_RUNTIME_ARRAY):
                    cur = targs[0]
                elif kind == OP_TYPE_MATRIX:
                    cur = targs[0]
                else:
                    cur = None
            self.pointer_types[ops[1]] = self.pointee(ops[0])

    def pointee(self, ptr_type_id):
        t = self.types.get(ptr_type_id)
        if t and t[0] == OP_TYPE_POINTER:
            return t[1][1]
        return None

    def type_size(self, tid, depth=0):
        t = self.types.get(tid)
        if t is None or depth > 32:
            return 0
        op, a = t
        if op == OP_TYPE_BOOL:
            return 4
        if op in (OP_TYPE_INT, OP_TYPE_FLOAT):
            return max(1, a[0] // 8)
        if op == OP_TYPE_VECTOR:
            return self.type_size(a[0], depth + 1) * a[1]
        if op == OP_TYPE_MATRIX:
            return self.type_size(a[0], depth + 1) * a[1]
        if op == OP_TYPE_ARRAY:
            return self.type_size(a[0], depth + 1) * self.constants.get(a[1], 0)
        if op == OP_TYPE_RUNTIME_ARRAY:
            return 0
        if op == OP_TYPE_STRUCT:
            return sum(self.type_size(m, depth + 1) for m in a)
        if op == OP_TYPE_POINTER:
            return 8
        return 0

    def threads(self):
        if self.local_size:
            return self.local_size[0] * self.local_size[1] * self.local_size[2]
        if self.local_size_ids:
            vals = [self.constants.get(i, 0) for i in self.local_size_ids]
            return vals[0] * vals[1] * vals[2]
        return 0

    def storage_bindings(self):
        total, writable = 0, 0
        for rtype, rid, sc in self.variables:
            pointee = self.pointee(rtype)
            is_ssbo = sc == SC_STORAGE_BUFFER
            if sc == SC_UNIFORM and pointee is not None:
                inner = pointee
                # arrays of blocks: unwrap
                while inner in self.types and self.types[inner][0] in (OP_TYPE_ARRAY, OP_TYPE_RUNTIME_ARRAY):
                    inner = self.types[inner][1][0]
                if DEC_BUFFER_BLOCK in self.decorations.get(inner, set()):
                    is_ssbo = True
            if not is_ssbo:
                continue
            total += 1
            var_dec = self.decorations.get(rid, set())
            inner = pointee
            while inner in self.types and self.types[inner][0] in (OP_TYPE_ARRAY, OP_TYPE_RUNTIME_ARRAY):
                inner = self.types[inner][1][0]
            members = self.types.get(inner, (None, ()))[1] if inner in self.types and self.types[inner][0] == OP_TYPE_STRUCT else ()
            all_members_ro = len(members) > 0 and all(m in self.member_nonwritable.get(inner, set()) for m in range(len(members)))
            if DEC_NON_WRITABLE in var_dec or all_members_ro:
                continue
            writable += 1
        return total, writable

    def shared_bytes(self):
        return sum(self.type_size(self.pointee(rtype)) for rtype, _rid, sc in self.variables if sc == SC_WORKGROUP)


def gate_file(path, args):
    with open(path, "rb") as f:
        data = f.read()
    if len(data) < 20 or len(data) % 4 != 0:
        return None, ["not a SPIR-V module (size)"]
    words = list(struct.unpack("<%dI" % (len(data) // 4), data))
    if words[0] != MAGIC:
        return None, ["not a SPIR-V module (magic 0x%08x)" % words[0]]
    m = Module(words)
    m.parse()
    total, writable = m.storage_bindings()
    report = {
        "file": os.path.basename(path), "bytes": len(data), "threads": m.threads(),
        "localSize": list(m.local_size) if m.local_size else None,
        "storageBindings": total, "writableStorageBindings": writable, "sharedBytes": m.shared_bytes(),
        "dynamicVectorOps": m.dynamic_vector_ops, "dynamicVectorChains": m.dynamic_vector_chains,
        "loops": m.loops, "entryPoints": m.entry_points, "instructions": m.instructions,
    }
    failures = []
    if report["bytes"] > args.max_bytes:
        failures.append("SPIR-V %d bytes > %d" % (report["bytes"], args.max_bytes))
    if report["threads"] == 0:
        failures.append("no LocalSize execution mode (not a compute module?)")
    elif report["threads"] > args.max_threads:
        failures.append("threads/workgroup %d > %d" % (report["threads"], args.max_threads))
    if writable > args.max_storage_bindings:
        failures.append("writable storage bindings %d > %d" % (writable, args.max_storage_bindings))
    if report["sharedBytes"] > args.max_shared:
        failures.append("workgroup memory %d B > %d" % (report["sharedBytes"], args.max_shared))
    if m.dynamic_vector_ops or m.dynamic_vector_chains:
        failures.append("dynamic vector component indexing (ops=%d chains=%d)" % (m.dynamic_vector_ops, m.dynamic_vector_chains))
    if m.entry_points == 0:
        failures.append("no entry point")
    report["pass"] = not failures
    return report, failures


def main(argv):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--max-bytes", type=int, default=128 * 1024)
    p.add_argument("--max-storage-bindings", type=int, default=8)
    p.add_argument("--max-threads", type=int, default=256)
    p.add_argument("--max-shared", type=int, default=16 * 1024)
    p.add_argument("--quiet", action="store_true")
    p.add_argument("files", nargs="+")
    args = p.parse_args(argv)
    rc = 0
    for path in args.files:
        try:
            report, failures = gate_file(path, args)
        except (ValueError, IndexError, struct.error) as e:
            print("FAIL %s: malformed SPIR-V (%s)" % (path, e))
            rc = max(rc, 2)
            continue
        if report is None:
            for f in failures:
                print("FAIL %s: %s" % (path, f))
            rc = max(rc, 2)
            continue
        if not args.quiet or failures:
            print("FS-SPV-GATE " + json.dumps(report, separators=(",", ":")))
        for f in failures:
            print("FAIL %s: %s" % (path, f))
        if failures:
            rc = max(rc, 1)
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
