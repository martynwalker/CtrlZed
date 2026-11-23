#!/usr/bin/env python3
"""Offline parser/provenance checks for the online-font catalogue generator."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


spec = importlib.util.spec_from_file_location("online_font_generator", Path(__file__).with_name("build-online-font-catalog.py"))
generator = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(generator)


class TextProtoTests(unittest.TestCase):
    def test_nested_names_utf8_comments_and_adjacent_strings(self):
        value = generator.parse_text_proto(r'''# not an instruction
name: "Top " "Family"
designer: "Zoë \302\251 \u00A9 \xC2\xA9"
fonts { name: "Nested" weight: 400 source < branch: "main" > }
subsets: ["latin", "greek"]
url: "https://example.invalid/a#b" // safe comment
'''.encode("utf-8"))
        self.assertEqual(value["name"], ["Top Family"])
        self.assertEqual(value["designer"], ["Zoë © © ©"])
        self.assertEqual(value["fonts"][0]["name"], ["Nested"])
        self.assertEqual(value["fonts"][0]["source"][0]["branch"], ["main"])
        self.assertEqual(value["subsets"], ["latin", "greek"])
        self.assertEqual(value["url"], ["https://example.invalid/a#b"])

    def test_reject_malformed_and_invalid_utf8(self):
        for raw in (b'name "missing colon"', b'name: "unterminated', b'fonts { name: "x"',
                    br'name: "\q"', br'name: "\777"', br'name: "\uD800"', b'name: "\xff"'):
            with self.subTest(raw=raw), self.assertRaises(generator.CatalogueError):
                generator.parse_text_proto(raw)

    def test_duplicate_unique_fields_are_not_silently_selected(self):
        value = generator.parse_text_proto(b'name: "one" name: "two"')
        with self.assertRaises(generator.CatalogueError):
            generator.text_field(value, "name")

    def test_bounds_and_paths(self):
        with self.assertRaises(generator.CatalogueError):
            generator.parse_text_proto(b" " * (generator.MAX_METADATA_BYTES + 1))
        for path in ("../font.ttf", "ofl/../font.ttf", "/font.ttf", "ofl\\font.ttf", "ofl/font.ttf\n", "C:font.ttf"):
            self.assertFalse(generator.safe_relative_path(path))
        self.assertTrue(generator.safe_relative_path("ofl/example/Example-Regular.ttf"))


class ManifestTests(unittest.TestCase):
    def fixture(self, axes=False, font_filename="Example-Regular.ttf", licence=True):
        metadata = '''name: "Example"
designer: "Zoë"
license: "OFL"
category: "SANS_SERIF"
fonts { name: "Example" style: "normal" weight: 400 filename: "%s"
post_script_name: "Example-Regular" full_name: "Example Regular" }
subsets: "latin"
subsets: "menu"
''' % font_filename
        if axes:
            metadata += 'axes { tag: "wght" min_value: 400 max_value: 700 }\n'
        files = {"ofl/example/METADATA.pb": metadata.encode("utf-8"),
                 "ofl/example/" + font_filename: b"not downloaded by generator"}
        if licence:
            # Mixed line endings and BOM are intentional provenance fixtures.
            files["ofl/example/OFL.txt"] = b"\xef\xbb\xbfCopyright Zo\xc3\xab\r\nSIL OPEN FONT LICENSE Version 1.1\nKeep original bytes.\r\n"
        tree = {path: generator.Blob("100644", generator.git_blob_sha(data), path) for path, data in files.items()}
        raw = {generator.git_blob_sha(data): data for data in files.values()}
        return tree, raw

    def test_metadata_faces_and_licence_hash_roundtrip(self):
        tree, raw = self.fixture()
        record, disposition = generator.family_record("ofl/example/METADATA.pb", tree, raw)
        self.assertEqual(disposition, "supported")
        self.assertEqual(record["name"], "Example")
        self.assertEqual(record["faces"][0]["fullName"], "Example Regular")
        self.assertEqual(record["faces"][0]["weight"], 400)
        self.assertEqual(record["subsets"], ["latin"])
        encoded = json.dumps(record, ensure_ascii=False).encode("utf-8")
        copied = json.loads(encoded)["licenceFiles"][0]
        self.assertEqual(generator.git_blob_sha(copied["text"].encode("utf-8")), copied["blobSha"])
        self.assertTrue(copied["text"].startswith("\ufeff"))
        self.assertIn("\r\n", copied["text"])

    def test_variable_and_missing_licence_are_distinct(self):
        for options, disposition in (({"axes": True}, "unsupported"),
                                     ({"font_filename": "Example[wght].ttf"}, "unsupported"),
                                     ({"licence": False}, "licence")):
            tree, raw = self.fixture(**options)
            record, actual = generator.family_record("ofl/example/METADATA.pb", tree, raw)
            self.assertIsNone(record)
            self.assertEqual(actual, disposition)

    def test_sparse_selection_never_includes_font_binary(self):
        tree, _ = self.fixture()
        paths = generator.metadata_and_licence_paths(tree)
        self.assertEqual(paths, ["ofl/example/METADATA.pb", "ofl/example/OFL.txt"])
        self.assertFalse(any(path.endswith(".ttf") for path in paths))

    def test_licence_not_inferred_from_directory_name(self):
        tree, raw = self.fixture()
        key = tree["ofl/example/OFL.txt"].sha
        raw[key] = b"Apache License Version 2.0"
        record, disposition = generator.family_record("ofl/example/METADATA.pb", tree, raw)
        self.assertIsNone(record)
        self.assertEqual(disposition, "licence")

    def test_unsupported_main_licence_filename_is_excluded_honestly(self):
        tree, raw = self.fixture()
        old = tree.pop("ofl/example/OFL.txt")
        renamed = "ofl/example/LICENSE.txt"
        tree[renamed] = generator.Blob(old.mode, old.sha, renamed)
        record, disposition = generator.family_record("ofl/example/METADATA.pb", tree, raw)
        self.assertIsNone(record)
        self.assertEqual(disposition, "licence")

    def test_source_counts_report_every_metadata_family_once(self):
        tree, raw = {}, {}
        for suffix, options in (("supported", {}), ("variable", {"axes": True}), ("unlicensed", {"licence": False})):
            extra_tree, extra_raw = self.fixture(**options)
            for path, blob in extra_tree.items():
                renamed = path.replace("ofl/example/", "ofl/" + suffix + "/")
                tree[renamed] = generator.Blob(blob.mode, blob.sha, renamed)
            raw.update(extra_raw)
        catalogue = generator.build_catalogue(Path("unused"), tree, raw)
        self.assertEqual(len(catalogue["families"]), 1)
        self.assertEqual(catalogue["source"]["metadataFamiliesScanned"], 3)
        self.assertEqual(catalogue["source"]["supportedFamilies"], 1)
        self.assertEqual(catalogue["source"]["excludedVariableOrUnsupported"], 1)
        self.assertEqual(catalogue["source"]["excludedMissingLicence"], 1)

    def test_canonical_batch_ignores_checkout_newline_conversion(self):
        with tempfile.TemporaryDirectory(prefix="ctrlzed-catalogue-test-") as temporary:
            repo = Path(temporary)
            generator.git(None, "init", str(repo))
            generator.git(repo, "config", "user.email", "fixture@example.invalid")
            generator.git(repo, "config", "user.name", "Offline fixture")
            generator.git(repo, "config", "core.autocrlf", "false")
            path = repo / "ofl" / "example" / "OFL.txt"
            path.parent.mkdir(parents=True)
            original = b"\xef\xbb\xbfLicence Zo\xc3\xab\r\nSecond line\n"
            path.write_bytes(original)
            generator.git(repo, "add", ".")
            generator.git(repo, "commit", "-m", "Offline canonical-byte fixture")
            commit = generator.git(repo, "rev-parse", "HEAD").decode("ascii").strip()
            tree = generator.tree_blobs(repo, commit)
            # Make the checkout deliberately differ; the manifest still uses
            # the actual immutable Git bytes, including BOM and mixed endings.
            path.write_bytes(b"A different checkout representation\r\n")
            canonical = generator.canonical_blobs(repo, ["ofl/example/OFL.txt"], tree)
            blob = tree["ofl/example/OFL.txt"]
            self.assertEqual(canonical[blob.sha], original)
            self.assertEqual(generator.git_blob_sha(canonical[blob.sha]), blob.sha)


if __name__ == "__main__":
    unittest.main()
