#!/usr/bin/env python3
"""Build CtrlZed's keyless, immutable Google Fonts static-face catalogue.

Only metadata/licence blobs are checked out. Font binaries are recorded by
their actual metadata paths and Git hashes; only the CI preview fixture is
downloaded. Canonical Git bytes avoid Windows checkout newline conversion.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import tempfile
from typing import Any, NamedTuple


PINNED_COMMIT = "06bfe0d62a98375cb09c942fa18e68bd32cff057"
REPOSITORY_URL = "https://github.com/google/fonts.git"
MAX_CATALOGUE_BYTES = 20 * 1024 * 1024
MAX_METADATA_BYTES = 512 * 1024
MAX_LICENCE_BYTES = 128 * 1024
FAMILY_PATH = re.compile(r"^(ofl|apache|ufl)/[a-z0-9][a-z0-9_-]{0,119}/METADATA\.pb$")
LICENCE_BASENAMES = {
    "ofl.txt", "ufl.txt", "license", "license.txt", "licence", "licence.txt",
    "copying", "copying.txt", "notice", "notice.txt", "copyright", "copyright.txt",
}
NUMBER = re.compile(r"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?")
IDENTIFIER = re.compile(r"[A-Za-z_][A-Za-z0-9_.+-]*")


class CatalogueError(ValueError):
    pass


class Token(NamedTuple):
    kind: str
    value: Any
    line: int


class Blob(NamedTuple):
    mode: str
    sha: str
    path: str


def git_blob_sha(data: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def tokens(text: str) -> list[Token]:
    result: list[Token] = []
    index, line = 0, 1
    while index < len(text):
        c = text[index]
        if c.isspace() or c == "\ufeff":
            line += c == "\n"
            index += 1
            continue
        if c == "#" or text.startswith("//", index):
            end = text.find("\n", index)
            index = len(text) if end < 0 else end
            continue
        if text.startswith("/*", index):
            end = text.find("*/", index + 2)
            if end < 0:
                raise CatalogueError(f"Unclosed comment at line {line}.")
            line += text[index:end + 2].count("\n")
            index = end + 2
            continue
        if c in "{}<>[]:,;":
            result.append(Token(c, c, line))
            index += 1
        elif c in "\"'":
            quote, start_line = c, line
            index += 1
            value = bytearray()
            while index < len(text) and text[index] != quote:
                c = text[index]
                if c in "\r\n":
                    raise CatalogueError(f"Unescaped newline in string at line {start_line}.")
                if c != "\\":
                    value.extend(c.encode("utf-8"))
                    index += 1
                    continue
                index += 1
                if index >= len(text):
                    raise CatalogueError(f"Unclosed escape at line {start_line}.")
                escape = text[index]
                simple = {"a": 7, "b": 8, "f": 12, "n": 10, "r": 13, "t": 9,
                          "v": 11, "\\": 92, "'": 39, '"': 34, "?": 63}
                if escape in simple:
                    value.append(simple[escape])
                    index += 1
                elif escape in "01234567":
                    match = re.match(r"[0-7]{1,3}", text[index:])
                    assert match is not None
                    number = int(match.group(), 8)
                    if number > 255:
                        raise CatalogueError(f"Octal byte escape exceeds 255 at line {start_line}.")
                    value.append(number)
                    index += len(match.group())
                elif escape in "xX":
                    match = re.match(r"[0-9a-fA-F]{1,2}", text[index + 1:])
                    if match is None:
                        raise CatalogueError(f"Invalid hex escape at line {start_line}.")
                    value.append(int(match.group(), 16))
                    index += 1 + len(match.group())
                elif escape in "uU":
                    count = 4 if escape == "u" else 8
                    digits = text[index + 1:index + 1 + count]
                    if len(digits) != count or not re.fullmatch(r"[0-9a-fA-F]+", digits):
                        raise CatalogueError(f"Invalid Unicode escape at line {start_line}.")
                    try:
                        value.extend(chr(int(digits, 16)).encode("utf-8"))
                    except (ValueError, UnicodeEncodeError) as error:
                        raise CatalogueError(f"Invalid Unicode scalar at line {start_line}.") from error
                    index += 1 + count
                else:
                    raise CatalogueError(f"Unknown string escape at line {start_line}.")
                if len(value) > 32768:
                    raise CatalogueError(f"String too long at line {start_line}.")
            if index >= len(text):
                raise CatalogueError(f"Unclosed string at line {start_line}.")
            index += 1
            if len(value) > 32768:
                raise CatalogueError(f"String too long at line {start_line}.")
            try:
                result.append(Token("string", value.decode("utf-8"), start_line))
            except UnicodeDecodeError as error:
                raise CatalogueError(f"String escapes are not UTF-8 at line {start_line}.") from error
        else:
            match = NUMBER.match(text, index)
            if match:
                raw = match.group()
                number = float(raw) if any(x in raw for x in ".eE") else int(raw)
                result.append(Token("number", number, line))
                index = match.end()
            else:
                match = IDENTIFIER.match(text, index)
                if match is None:
                    raise CatalogueError(f"Unexpected character {c!r} at line {line}.")
                result.append(Token("identifier", match.group(), line))
                index = match.end()
        if len(result) > 100000:
            raise CatalogueError("Metadata token limit exceeded.")
    result.append(Token("eof", None, line))
    return result


class TextProtoParser:
    def __init__(self, text: str):
        self.items = tokens(text)
        self.index = 0

    @property
    def token(self) -> Token:
        return self.items[self.index]

    def take(self, kind: str) -> Token:
        token = self.token
        if token.kind != kind:
            raise CatalogueError(f"Expected {kind} at line {token.line}, got {token.kind}.")
        self.index += 1
        return token

    def scalar(self) -> Any:
        if self.token.kind == "string":
            value = ""
            while self.token.kind == "string":
                value += self.take("string").value
            return value
        if self.token.kind in ("number", "identifier"):
            token = self.token
            self.index += 1
            return token.value
        raise CatalogueError(f"Expected scalar at line {self.token.line}.")

    def value(self, depth: int) -> Any:
        if depth > 32:
            raise CatalogueError("Metadata nesting limit exceeded.")
        if self.token.kind in ("{", "<"):
            opener = self.token.kind
            self.index += 1
            return self.message("}" if opener == "{" else ">", depth + 1)
        if self.token.kind == "[":
            self.index += 1
            values = []
            while self.token.kind != "]":
                values.append(self.value(depth + 1))
                if self.token.kind == ",":
                    self.index += 1
                elif self.token.kind != "]":
                    raise CatalogueError(f"Expected list separator at line {self.token.line}.")
            self.index += 1
            return values
        return self.scalar()

    def message(self, end: str = "eof", depth: int = 0) -> dict[str, list[Any]]:
        result: dict[str, list[Any]] = {}
        while self.token.kind != end:
            if self.token.kind == "eof":
                raise CatalogueError(f"Unclosed message at line {self.token.line}.")
            key = self.take("identifier").value
            colon = self.token.kind == ":"
            if colon:
                self.index += 1
            elif self.token.kind not in ("{", "<"):
                raise CatalogueError(f"Expected ':' at line {self.token.line}.")
            value = self.value(depth + 1)
            result.setdefault(key, []).extend(value if isinstance(value, list) else [value])
            if self.token.kind in (";", ","):
                self.index += 1
        self.take(end)
        return result


def parse_text_proto(data: bytes) -> dict[str, list[Any]]:
    if len(data) > MAX_METADATA_BYTES:
        raise CatalogueError("Metadata byte limit exceeded.")
    try:
        return TextProtoParser(data.decode("utf-8")).message()
    except UnicodeDecodeError as error:
        raise CatalogueError("Metadata is not UTF-8.") from error


def one(record: dict[str, list[Any]], key: str, required: bool = True) -> Any:
    values = record.get(key, [])
    if not values and not required:
        return ""
    if len(values) != 1:
        raise CatalogueError(f"Expected exactly one {key!r} field.")
    return values[0]


def text_field(record: dict[str, list[Any]], key: str, required: bool = True, maximum: int = 4096) -> str:
    value = one(record, key, required)
    if not isinstance(value, str) or (required and not value.strip()) or len(value) > maximum:
        raise CatalogueError(f"Invalid {key!r} text.")
    if any(ord(c) < 32 for c in value):
        raise CatalogueError(f"Control character in {key!r}.")
    return value


def safe_relative_path(value: str) -> bool:
    path = PurePosixPath(value)
    return bool(value) and not path.is_absolute() and "\\" not in value and ":" not in value and \
        not any(c in value for c in "\r\n\0") and all(part not in ("", ".", "..") for part in value.split("/"))


def git(repo: Path | None, *arguments: str, data: bytes | None = None, timeout: int = 300) -> bytes:
    command = ["git"] + (["-C", str(repo)] if repo is not None else []) + list(arguments)
    environment = dict(os.environ, GIT_TERMINAL_PROMPT="0", GIT_LFS_SKIP_SMUDGE="1")
    completed = subprocess.run(command, input=data, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               env=environment, timeout=timeout, check=False)
    if completed.returncode:
        detail = completed.stderr.decode("utf-8", errors="replace")[-3000:]
        raise CatalogueError(f"Git command failed ({' '.join(arguments[:3])}): {detail}")
    return completed.stdout


def tree_blobs(repo: Path, commit: str) -> dict[str, Blob]:
    result: dict[str, Blob] = {}
    # Deliberately no -l: object-size queries can fetch every promisor font blob.
    for item in git(repo, "ls-tree", "-rz", "--full-tree", commit).split(b"\0"):
        if not item:
            continue
        head, raw_path = item.split(b"\t", 1)
        mode, kind, sha = head.decode("ascii").split()
        path = raw_path.decode("utf-8")
        if kind == "blob" and mode in ("100644", "100755") and safe_relative_path(path):
            result[path] = Blob(mode, sha, path)
    return result


def metadata_and_licence_paths(tree: dict[str, Blob]) -> list[str]:
    families = {str(PurePosixPath(path).parent) for path in tree if FAMILY_PATH.fullmatch(path)}
    return sorted(path for path in tree if FAMILY_PATH.fullmatch(path) or
                  (str(PurePosixPath(path).parent) in families and
                   PurePosixPath(path).name.casefold() in LICENCE_BASENAMES))


def canonical_blobs(repo: Path, paths: list[str], tree: dict[str, Blob]) -> dict[str, bytes]:
    hashes = sorted({tree[path].sha for path in paths})
    output = git(repo, "cat-file", "--batch", data=("\n".join(hashes) + "\n").encode("ascii"))
    values: dict[str, bytes] = {}
    offset = 0
    for expected in hashes:
        end = output.find(b"\n", offset)
        if end < 0:
            raise CatalogueError("Truncated canonical Git batch output.")
        header = output[offset:end].decode("ascii").split()
        if len(header) != 3 or header[:2] != [expected, "blob"]:
            raise CatalogueError("A requested metadata/licence Git blob was unavailable.")
        size = int(header[2])
        if size > MAX_METADATA_BYTES or size < 0:
            raise CatalogueError("A metadata/licence Git blob exceeded the byte limit.")
        offset = end + 1
        value = output[offset:offset + size]
        if len(value) != size or output[offset + size:offset + size + 1] != b"\n" or git_blob_sha(value) != expected:
            raise CatalogueError("Canonical Git blob failed length/hash validation.")
        values[expected] = value
        offset += size + 1
    if offset != len(output):
        raise CatalogueError("Unexpected trailing canonical Git batch output.")
    return values


def licence_matches(code: str, value: str) -> bool:
    folded = value.casefold()
    return {"OFL": "sil open font license" in folded and "1.1" in folded,
            "APACHE2": "apache license" in folded and "2.0" in folded,
            "UFL": ("ubuntu font licence" in folded or "ubuntu font license" in folded)}.get(code, False)


def family_record(path: str, tree: dict[str, Blob], raw: dict[str, bytes],
                  licence_index: dict[str, list[str]] | None = None) -> tuple[dict[str, Any] | None, str]:
    metadata = parse_text_proto(raw[tree[path].sha])
    family_id = str(PurePosixPath(path).parent)
    name = text_field(metadata, "name", maximum=300)
    designer = text_field(metadata, "designer", False, 2000)
    code = text_field(metadata, "license")
    category = text_field(metadata, "category", maximum=80).lower().replace("_", "-")
    faces = []
    face_keys = set()
    for record in metadata.get("fonts", []):
        if not isinstance(record, dict):
            raise CatalogueError("A font record is not a message.")
        filename = text_field(record, "filename")
        style = text_field(record, "style")
        weight = one(record, "weight")
        face_path = family_id + "/" + filename
        if style not in ("normal", "italic") or weight not in (400, 700) or len(face_path) > 500 or \
                not isinstance(weight, int) or not filename.endswith(".ttf") or \
                any(c in filename for c in "[]/\\") or "variable" in filename.casefold() or \
                re.search(r"(?:^|[-_.])vf(?:[-_.]|$)", filename, re.IGNORECASE) or \
                not safe_relative_path(face_path) or face_path not in tree:
            continue
        # Variable family metadata can have unusual filenames, so axis-bearing
        # records are conservatively excluded even without bracketed filenames.
        if metadata.get("axes"):
            continue
        key = (style, weight)
        if key in face_keys:
            raise CatalogueError("Ambiguous duplicate standard face in family metadata.")
        face_keys.add(key)
        faces.append({"path": face_path, "blobSha": tree[face_path].sha,
                      "name": text_field(record, "name", maximum=300), "fullName": text_field(record, "full_name", maximum=500),
                      "postScriptName": text_field(record, "post_script_name", maximum=300), "style": style, "weight": weight})
    if not faces:
        return None, "unsupported"
    candidates = []
    licence_paths = licence_index.get(family_id, []) if licence_index is not None else sorted(
        candidate for candidate in tree if str(PurePosixPath(candidate).parent) == family_id and
        PurePosixPath(candidate).name.casefold() in LICENCE_BASENAMES)
    for candidate in licence_paths:
        data = raw.get(tree[candidate].sha)
        if data is None or not data or len(data) > MAX_LICENCE_BYTES:
            continue
        try:
            value = data.decode("utf-8")
        except UnicodeDecodeError:
            continue
        candidates.append({"path": candidate, "blobSha": tree[candidate].sha, "text": value})
    expected_code = {"ofl": "OFL", "apache": "APACHE2", "ufl": "UFL"}[family_id.split("/", 1)[0]]
    expected_names = {"OFL": {"ofl.txt"}, "UFL": {"ufl.txt"},
                      "APACHE2": {"license", "license.txt", "licence", "licence.txt"}}
    if code != expected_code or not any(
            PurePosixPath(candidate["path"]).name.casefold() in expected_names.get(code, set()) and
            licence_matches(code, candidate["text"]) for candidate in candidates):
        return None, "licence"
    subsets = sorted({value for value in metadata.get("subsets", []) if isinstance(value, str) and value != "menu"})
    if len(subsets) > 150 or any(not value or len(value) > 80 or any(ord(c) < 32 for c in value) for value in subsets):
        raise CatalogueError("Subset limits exceeded.")
    tags = {category}
    for key in ("stroke", "classifications"):
        tags.update(value.lower().replace("_", "-") for value in metadata.get(key, []) if isinstance(value, str))
    tags.update({"sans-serif": ["sans"], "handwriting": ["script"], "monospace": ["technical"]}.get(category, []))
    if len(tags) > 32 or any(not value or len(value) > 40 or any(ord(c) < 32 for c in value) for value in tags):
        raise CatalogueError("Tag limits exceeded.")
    return {"id": family_id, "name": name, "designer": designer,
            "category": category, "subsets": subsets, "tags": sorted(tags), "licence": code,
            "metadataPath": path, "metadataBlobSha": tree[path].sha,
            "faces": sorted(faces, key=lambda face: (face["weight"], face["style"] != "normal")),
            "licenceFiles": candidates}, "supported"


def build_catalogue(repo: Path, tree: dict[str, Blob], raw: dict[str, bytes]) -> dict[str, Any]:
    paths = sorted(path for path in tree if FAMILY_PATH.fullmatch(path))
    licence_index: dict[str, list[str]] = {}
    for path in sorted(tree):
        if PurePosixPath(path).name.casefold() in LICENCE_BASENAMES:
            licence_index.setdefault(str(PurePosixPath(path).parent), []).append(path)
    supported, unsupported, missing_licence = [], 0, 0
    for path in paths:
        try:
            record, disposition = family_record(path, tree, raw, licence_index)
            if record is not None:
                supported.append(record)
            elif disposition == "licence":
                missing_licence += 1
            else:
                unsupported += 1
        except (CatalogueError, KeyError, UnicodeError) as error:
            unsupported += 1
            print(f"Excluded unsupported metadata {path}: {error}", file=sys.stderr)
    supported.sort(key=lambda record: (record["name"].casefold(), record["id"]))
    if not paths or not supported:
        raise CatalogueError("The pinned source produced no supported font families.")
    return {"schemaVersion": 1,
            "source": {"repository": "google/fonts", "commit": PINNED_COMMIT,
                       "generatedUtc": dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z"),
                       "metadataFamiliesScanned": len(paths), "supportedFamilies": len(supported),
                       "excludedVariableOrUnsupported": unsupported, "excludedMissingLicence": missing_licence},
            "scope": "Static Google Fonts faces supported by CtrlZed; source snapshot, not the entire live catalogue.",
            "families": supported}


def fixture(repo: Path, catalogue: dict[str, Any], directory: Path,
            family_name: str = "Aclonica", prefix: str = "") -> None:
    family = next((item for item in catalogue["families"] if item["name"] == family_name), None)
    if family is None:
        raise CatalogueError(f"Pinned {family_name} fixture is absent from the supported catalogue.")
    face = next((item for item in family["faces"] if item["style"] == "normal" and item["weight"] == 400), None)
    if face is None:
        raise CatalogueError(f"Pinned {family_name} fixture has no standard normal face.")
    data = git(repo, "show", PINNED_COMMIT + ":" + face["path"])
    if git_blob_sha(data) != face["blobSha"] or len(data) > 32 * 1024 * 1024 or data[:4] not in (b"\x00\x01\x00\x00", b"true"):
        raise CatalogueError("Pinned fixture font failed hash/TrueType validation.")
    if len(data) < 12 or 12 + int.from_bytes(data[4:6], "big") * 16 > len(data):
        raise CatalogueError("Pinned fixture has an invalid TrueType table directory.")
    for offset in range(12, 12 + int.from_bytes(data[4:6], "big") * 16, 16):
        if data[offset:offset + 4] == b"fvar":
            raise CatalogueError("Pinned fixture is variable and unsupported by this release.")
    directory.mkdir(parents=True, exist_ok=True)
    font_name = family_name + "-Regular.ttf"
    (directory / font_name).write_bytes(data)
    licence_directory = directory / "secondary-licences" if prefix else directory
    licence_directory.mkdir(parents=True, exist_ok=True)
    for item in family["licenceFiles"]:
        (licence_directory / PurePosixPath(item["path"]).name).write_bytes(item["text"].encode("utf-8"))
    (directory / (prefix + "fixture.json")).write_text(json.dumps(family, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (directory / (prefix + "fixture-receipt.json")).write_text(json.dumps({"sourceCommit": PINNED_COMMIT,
        "familyId": family["id"], "fontFile": font_name, "sourcePath": face["path"],
        "blobSha": face["blobSha"], "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data),
        "licenceFiles": [PurePosixPath(item["path"]).name for item in family["licenceFiles"]]}, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path("OnlineFontCatalogue.json"))
    parser.add_argument("--fixture-directory", type=Path)
    parser.add_argument("--source-directory", type=Path, help="Existing partial source checkout; immutable pin is still enforced.")
    args = parser.parse_args()
    try:
        if shutil.which("git") is None:
            raise CatalogueError("Git is required to generate the online font catalogue.")
        with tempfile.TemporaryDirectory(prefix="ctrlzed-google-fonts-") as temporary:
            repo = args.source_directory.resolve() if args.source_directory else Path(temporary) / "fonts"
            if not args.source_directory:
                git(None, "clone", "--filter=blob:none", "--no-checkout", "--depth=1", "--single-branch", REPOSITORY_URL, str(repo))
            git(repo, "fetch", "--filter=blob:none", "--depth=1", "origin", PINNED_COMMIT)
            actual = git(repo, "rev-parse", PINNED_COMMIT + "^{commit}").decode("ascii").strip()
            if actual != PINNED_COMMIT:
                raise CatalogueError("Source commit does not match the immutable catalogue pin.")
            tree = tree_blobs(repo, PINNED_COMMIT)
            selected = metadata_and_licence_paths(tree)
            git(repo, "sparse-checkout", "init", "--no-cone")
            git(repo, "sparse-checkout", "set", "--no-cone", "--stdin", data=("\n".join("/" + path for path in selected) + "\n").encode("utf-8"))
            git(repo, "checkout", "--detach", PINNED_COMMIT)
            raw = canonical_blobs(repo, selected, tree)
            catalogue = build_catalogue(repo, tree, raw)
            encoded = (json.dumps(catalogue, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
            if len(encoded) > MAX_CATALOGUE_BYTES:
                raise CatalogueError("Generated catalogue exceeds 20 MiB; refusing a truncated catalogue.")
            if args.fixture_directory:
                fixture(repo, catalogue, args.fixture_directory)
                if any(item["name"] == "Lato" for item in catalogue["families"]):
                    fixture(repo, catalogue, args.fixture_directory, "Lato", "secondary-")
            args.output.parent.mkdir(parents=True, exist_ok=True)
            temporary_output = args.output.with_name(args.output.name + ".tmp")
            temporary_output.write_bytes(encoded)
            temporary_output.replace(args.output)
            print(json.dumps({"output": str(args.output), "bytes": len(encoded),
                              "catalogueSha256": hashlib.sha256(encoded).hexdigest(), **catalogue["source"]}))
        return 0
    except (CatalogueError, subprocess.SubprocessError, OSError) as error:
        print(f"Online font catalogue generation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
