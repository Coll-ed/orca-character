#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""pcktool —— 读写 Godot PCK（本项目的资源包格式）。

## 格式（实测确认，非假设）
`SlayTheSpire2` 用的是**标准 Godot PCK v2**（本项目 .pck 亦然）。头部实测：

    @0   magic       4 B    b"GDPC"
    @4   pack_ver    u32    3
    @8   ver_major   u32    4
    @12  ver_minor   u32    5
    @16  ver_patch   u32    1
    @20  flags        u32    2 = PACK_REL_FILEBASE（路径相对 file_base）
    @24  file_base   u64    数据区起点
    @32  dir_offset  u64    目录区起点
    @40  reserved    16 × u32

    目录区（位于 dir_offset）：
        file_count  u32
        条目 × N：
            path_len  u32
            path      path_len 字节，**不补齐**（实测），可能带尾 NUL
            offset    u64   （相对 file_base）
            size      u64
            md5       16 B
            flags     u32

> 交叉印证：本项目 pck 实测 `file_base=112`、`dir_offset=54,600,582`、
> 目录首字段=340 —— 与旧 PckTool 输出的 `340 个文件 / dir_offset=54600582` 完全一致。

## 用法
    python pcktool.py list   <pck>
    python pcktool.py unpack <pck> <输出目录>
    python pcktool.py pack   <输入目录> <pck>     # 待实现

## 纪律
- 格式常量集中在 `FMT`，**不散落魔法数**
- 边界显式：magic 不对、越界、条目残缺 → 抛异常，不静默跳过
"""
from __future__ import annotations

import argparse
import hashlib
import os
import struct
import sys

# Windows 控制台默认 GBK：打印非 GBK 字符（中文/✅）会 UnicodeEncodeError 直接崩脚本。
# errors="replace" 保证「永不因编码而崩」——这是边界显式，不是美化。
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

# ── 格式常量（唯一来源；数值来源见模块 docstring 的实测记录）────────────────
MAGIC = b"GDPC"
FLAG_REL_FILEBASE = 2          # reserved[0] 的取值，表示路径相对 file_base
MD5_LEN = 16
PACK_VERSION = 3               # 实测原件的 pack 版本
GODOT_VERSION = (4, 5, 1)      # 实测原件写的是 Godot 4.5.1
ENTRY_FLAGS = 0                # 实测 340 条全部为 0
FILE_BASE = 112                # hardcode-ok：实测原件的 file_base；头 104 B + 8 B 对齐填充
MIN_HEADER = 40 + 16 * 4       # magic..reserved 的总字节数
DIR_ENTRY_FIXED = 4 + 8 + 8 + MD5_LEN + 4        # 条目里除路径外的固定部分

# ── v2 头字段偏移 ──────────────────────────────────────────────────────────
OFF_PACK_VER = 4
OFF_FILE_BASE = 24
OFF_DIR_OFFSET = 32


class PckError(RuntimeError):
    """pck 结构不符合预期（magic 错、越界、截断）。"""


class Entry:
    __slots__ = ("path", "offset", "size", "md5", "flags")

    def __init__(self, path: str, offset: int, size: int, md5: bytes, flags: int):
        self.path, self.offset, self.size, self.md5, self.flags = path, offset, size, md5, flags

    def __repr__(self) -> str:
        return f"<Entry {self.path} @{self.offset} {self.size}B>"


def read_index(pck_path: str) -> tuple[dict, list[Entry]]:
    """读取 pck 头部与目录；返回 (header 信息, 条目表)。"""
    size = os.path.getsize(pck_path)
    if size < MIN_HEADER:
        raise PckError(f"文件太小（{size} B < 最小头 {MIN_HEADER} B）：{pck_path}")

    with open(pck_path, "rb") as f:
        head = f.read(MIN_HEADER)
        if head[:4] != MAGIC:
            raise PckError(f"magic 不是 GDPC，实际 {head[:4]!r}：{pck_path}")

        pack_ver = struct.unpack_from("<I", head, OFF_PACK_VER)[0]
        ver = struct.unpack_from("<III", head, 8)
        flags, file_base, dir_offset = struct.unpack_from("<IQQ", head, 20)

        if not (0 < file_base <= dir_offset <= size):
            raise PckError(f"头字段越界：file_base={file_base} dir_offset={dir_offset} size={size}")

        f.seek(dir_offset)
        (count,) = struct.unpack("<I", f.read(4))
        entries: list[Entry] = []
        for i in range(count):
            raw = f.read(4)
            if len(raw) < 4:
                raise PckError(f"目录截断：第 {i} 个条目的 path_len 读不到")
            (plen,) = struct.unpack("<I", raw)
            raw_path = f.read(plen)
            # 实测：路径**不补齐**（align4=False 是唯一自洽解），且可能带尾 NUL
            path = raw_path.split(b"\x00", 1)[0].decode("utf-8")
            off, sz = struct.unpack("<QQ", f.read(16))
            md5 = f.read(MD5_LEN)
            (eflags,) = struct.unpack("<I", f.read(4))
            entries.append(Entry(path, off, sz, md5, eflags))

        header = {
            "pack_version": pack_ver,
            "godot": ".".join(str(v) for v in ver),
            "flags": flags,
            "file_base": file_base,
            "dir_offset": dir_offset,
            "count": count,
            "size": size,
        }
        return header, entries


def cmd_list(pck: str) -> int:
    header, entries = read_index(pck)
    print(f"{pck}")
    print(f"  Godot {header['godot']} / pack v{header['pack_version']} / flags={header['flags']}"
          f"{' (相对路径)' if header['flags'] & FLAG_REL_FILEBASE else ''}")
    print(f"  file_base={header['file_base']:,}  dir_offset={header['dir_offset']:,}  "
          f"文件数={header['count']}  总大小={header['size']:,} B")

    # 完整性自检：数据区必须落在 [file_base, dir_offset) 内且互不重叠
    spans = sorted((e.offset, e.offset + e.size, e.path) for e in entries)
    overflows = [s for s in spans if s[1] > header["dir_offset"]]
    bad_off = [s for s in spans if s[0] < 0]
    overlap = [(a, b) for a, b in zip(spans, spans[1:]) if a[1] > b[0]]
    print(f"  自检：越界 {len(overflows)} / 负偏移 {len(bad_off)} / 重叠 {len(overlap)}")
    if overflows or bad_off or overlap:
        raise PckError("目录自检失败（见上）")

    by_ext: dict[str, int] = {}
    for e in entries:
        by_ext[os.path.splitext(e.path)[1].lower()] = by_ext.get(os.path.splitext(e.path)[1].lower(), 0) + 1
    print("  扩展名分布（前 12）：")
    for ext, n in sorted(by_ext.items(), key=lambda kv: -kv[1])[:12]:
        print(f"    {ext or '(无)':<12} {n}")
    return 0


def cmd_unpack(pck: str, outdir: str) -> int:
    header, entries = read_index(pck)
    base = header["file_base"]
    os.makedirs(outdir, exist_ok=True)
    written = 0
    with open(pck, "rb") as f:
        for e in entries:
            dst = os.path.join(outdir, e.path.replace("/", os.sep))
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            f.seek(base + e.offset)
            data = f.read(e.size)
            if len(data) != e.size:
                raise PckError(f"读取 {e.path} 时截断：期望 {e.size} B，实得 {len(data)} B")
            with open(dst, "wb") as g:
                g.write(data)
            written += 1
    print(f"解出 {written} / {header['count']} 个文件 → {outdir}")
    if written != header["count"]:
        raise PckError(f"解出数量与目录条数不一致（{written} != {header['count']}）")
    return 0


def cmd_pack(srcdir: str, pck: str) -> int:
    """把目录打包成 Godot PCK v3。

    布局（与原件逐字段对齐，依据见模块 docstring 的实测记录）：
        [0, 104)        头（magic/版本/flags/file_base/dir_offset/reserved）
        [104, file_base) 零填充
        [file_base, ..)  文件数据，**顺序紧凑**、无空隙（原件实测末端 == dir_offset-file_base）
        [dir_offset, ..) 目录：count + 条目表（路径**不补齐**）
    """
    files: list[tuple[str, str]] = []          # (posix 路径, 绝对路径)
    for root, _, names in os.walk(srcdir):
        for n in names:
            abs_p = os.path.join(root, n)
            rel = os.path.relpath(abs_p, srcdir).replace(os.sep, "/")
            files.append((rel, abs_p))
    if not files:
        raise PckError(f"目录里没有文件：{srcdir}")
    files.sort()                               # 确定性输出（同一输入必得同一产物）

    # 先算尺寸，才能定位 dir_offset
    sizes = [(rel, os.path.getsize(abs_p)) for rel, abs_p in files]
    data_total = sum(sz for _, sz in sizes)
    dir_offset = FILE_BASE + data_total

    os.makedirs(os.path.dirname(os.path.abspath(pck)), exist_ok=True)
    with open(pck, "wb") as f:
        head = bytearray(MIN_HEADER)
        head[0:4] = MAGIC
        struct.pack_into("<I", head, OFF_PACK_VER, PACK_VERSION)
        struct.pack_into("<III", head, 8, *GODOT_VERSION)
        struct.pack_into("<IQQ", head, 20, FLAG_REL_FILEBASE, FILE_BASE, dir_offset)
        f.write(head)
        f.write(b"\x00" * (FILE_BASE - MIN_HEADER))       # 头尾对齐填充

        entries = []
        offset = 0
        for (rel, abs_p), (_, sz) in zip(files, sizes):
            with open(abs_p, "rb") as g:
                data = g.read()
            if len(data) != sz:
                raise PckError(f"{rel} 读取长度与 stat 不符（{len(data)} != {sz}）")
            f.write(data)
            entries.append(Entry(rel, offset, sz, hashlib.md5(data).digest(), ENTRY_FLAGS))
            offset += sz

        if f.tell() != dir_offset:
            raise PckError(f"数据区末端 {f.tell()} != dir_offset {dir_offset}（内部不一致）")

        f.write(struct.pack("<I", len(entries)))
        for e in entries:
            # 实测：原件的 path_len **含尾 NUL**（目录区 35,184 B = 34,844 + 340 条 × 1 B）；
            # 不写这个 NUL 会让每条目少 1 字节，产物与原件不再字节对齐。
            raw = e.path.encode("utf-8") + b"\x00"
            f.write(struct.pack("<I", len(raw)))
            f.write(raw)
            f.write(struct.pack("<QQ", e.offset, e.size))
            f.write(e.md5)
            f.write(struct.pack("<I", e.flags))

    print(f"打包 {len(files)} 个文件 → {pck}（{os.path.getsize(pck):,} B，dir_offset={dir_offset:,}）")
    return 0


def cmd_verify(a: str, b: str) -> int:
    """比较两个 pck 的「路径 → 尺寸/内容哈希」集合是否一致（偏移允许不同）。"""
    ha, ea = read_index(a)
    hb, eb = read_index(b)
    fa = {e.path: (e.size, e.md5) for e in ea}
    fb = {e.path: (e.size, e.md5) for e in eb}
    only_a = sorted(set(fa) - set(fb))
    only_b = sorted(set(fb) - set(fa))
    diff = sorted(p for p in set(fa) & set(fb) if fa[p] != fb[p])
    print(f"  A: {len(fa)} 条  B: {len(fb)} 条")
    print(f"  仅在 A: {len(only_a)}   仅在 B: {len(only_b)}   内容不符: {len(diff)}")
    for p in (only_a + only_b + diff)[:10]:
        print(f"    · {p}")
    if only_a or only_b or diff:
        raise PckError("往返校验失败：重打包产物与原件不一致")
    print("  ✅ 往返校验通过（路径 / 尺寸 / MD5 全一致）")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="Godot PCK 读写工具")
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("list");   p.add_argument("pck")
    p = sub.add_parser("unpack"); p.add_argument("pck"); p.add_argument("outdir")
    p = sub.add_parser("pack");   p.add_argument("srcdir"); p.add_argument("pck")
    p = sub.add_parser("verify"); p.add_argument("a"); p.add_argument("b")
    args = ap.parse_args()

    if args.cmd == "list":
        return cmd_list(args.pck)
    if args.cmd == "unpack":
        return cmd_unpack(args.pck, args.outdir)
    if args.cmd == "verify":
        return cmd_verify(args.a, args.b)
    return cmd_pack(args.srcdir, args.pck)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except PckError as e:
        print(f"pck 错误：{e}", file=sys.stderr)
        sys.exit(2)
