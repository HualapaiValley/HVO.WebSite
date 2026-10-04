"""Meaningful extraction/resolution failures against an owned disposable Git fixture."""
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("docs_navigation", Path(__file__).with_name("docs-navigation.py"))
navigation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(navigation)


class NavigationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="hvo-docs-fixture-")
        self.root = Path(self.temporary.name)
        self.git("init", "--quiet")
        self.write("docs/target file.md", '# Same\n\n# Same\n\n# Same-1\n\n<a id="custom"></a>\n')
        self.write("docs/folder/README.md", "# Folder\n")
        self.write("asset.svg", "<svg/>\n")
        self.write("code.cs", "line one\nline two\n")
        self.write("docs/source.md", "# Source\n")
        (self.root / "alias").symlink_to("docs", target_is_directory=True)
        self.git("add", ".")
        self.git("-c", "user.name=Navigation fixture", "-c", "user.email=fixture@example.invalid", "commit", "--quiet", "-m", "owned fixture")

    def tearDown(self):
        self.temporary.cleanup()

    def git(self, *args):
        return subprocess.check_output(["git", "-C", str(self.root), *args], stderr=subprocess.STDOUT)

    def write(self, path, text):
        file = self.root / path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(text)

    def inspect(self, content, ref=None):
        self.write("docs/source.md", content)
        return navigation.inspect(self.root, ref)

    def assertClean(self, content):
        report = self.inspect(content)
        self.assertEqual([], report["failures"])
        self.assertEqual([], report["qualifications"])
        return report

    def test_reference_link_percent_encoded_path_and_fragment(self):
        report = self.assertClean("[caption][id]\n\n[id]: target%20file.md#same-1\n")
        self.assertEqual(1, report["referenceDefinitions"])
        self.assertEqual("docs/target file.md", report["links"][0]["target"])

    def test_image_and_html_src_are_real_navigation(self):
        report = self.assertClean('![sample](../asset.svg)\n\n<img src="../asset.svg">\n')
        self.assertEqual(2, len(report["links"]))

    def test_html_custom_id_and_name(self):
        self.write("docs/named.md", '<a name="old"></a>\n')
        self.assertClean('[custom](target%20file.md#custom) [old](named.md#old)\n')

    def test_duplicate_heading_collisions(self):
        report = self.assertClean('[a](target%20file.md#same) [b](target%20file.md#same-1) [c](target%20file.md#same-1-1)\n')
        self.assertEqual(3, len(report["links"]))

    def test_fenced_inline_and_indented_examples_are_not_links(self):
        report = self.assertClean('`[sample](absent.md)`\n\n```html\n<a href="absent.md">x</a>\n```\n\n    [sample](absent.md)\n')
        self.assertEqual([], report["links"])
        self.assertEqual(2, report["illustrativeCodeBlocks"])

    def test_generated_content_only_exempts_its_illustration(self):
        report = self.assertClean('<generated_content>\n<a href="absent.md">example</a>\n</generated_content>\n\n[real](../asset.svg)\n')
        self.assertEqual(1, len(report["links"]))

    def test_archive_body_is_not_exempt(self):
        self.write("docs/archive/history.md", "# Historical\n\n[actual](absent.md)\n")
        report = self.inspect("# Source\n")
        self.assertEqual("docs/archive/history.md", report["failures"][0]["source"])

    def test_unclosed_illustration_cannot_silently_hide_navigation(self):
        report = self.inspect('<generated_content>\n\n[hidden](absent.md)\n')
        self.assertEqual("unclosed-illustration-wrapper", report["failures"][0]["status"])

    def test_missing_relative_path_fails(self):
        self.assertEqual("missing-relative-target", self.inspect('[missing](absent.md)\n')["failures"][0]["status"])

    def test_missing_fragment_fails(self):
        self.assertEqual("missing-anchor", self.inspect('[missing](target%20file.md#absent)\n')["failures"][0]["status"])

    def test_directory_readme_and_root_relative_path(self):
        self.assertClean('[folder](folder/#folder) [root](/asset.svg)\n')

    def test_relative_symlink_and_parent_resolution(self):
        self.assertClean('[alias](../alias/target%20file.md#same) [parent](../alias/../asset.svg)\n')

    def test_symlink_loop_fails(self):
        (self.root / "loop").symlink_to("loop")
        self.assertEqual("symlink-loop", self.inspect('[loop](../loop/file.md)\n')["failures"][0]["status"])

    def test_repository_escape_fails(self):
        self.assertEqual("escapes-repository", self.inspect('[outside](../../outside.md)\n')["failures"][0]["status"])

    def test_absolute_symlink_fails(self):
        (self.root / "outside").symlink_to("/tmp")
        self.assertEqual("absolute-symlink", self.inspect('[outside](../outside/file.md)\n')["failures"][0]["status"])

    def test_source_line_bounds(self):
        self.assertClean('[source](../code.cs#L1-L2)\n')
        for fragment in ("L0", "L3", "L2-L1"):
            with self.subTest(fragment=fragment):
                self.assertEqual("source-line-out-of-range", self.inspect(f'[bad](../code.cs#{fragment})\n')["failures"][0]["status"])

    def test_unsupported_binary_fragment_fails(self):
        self.assertEqual("unsupported-fragment", self.inspect('[bad](../asset.svg#unqualified)\n')["failures"][0]["status"])

    def test_same_file_heading_with_code_and_punctuation(self):
        self.assertClean('# RecordedAt: `UTC` (v9)\n\n[local](#recordedat-utc-v9)\n')

    def test_unicode_consumption_requires_manual_qualification(self):
        report = self.inspect('# One — Two\n\n[local](#one--two)\n')
        self.assertEqual([], report["failures"])
        self.assertEqual(1, len(report["qualifications"]))

    def test_external_url_and_undefined_reference_are_not_relative_proof(self):
        report = self.assertClean('[external](https://example.invalid/a) [undefined][missing]\n')
        self.assertEqual(1, len(report["links"]))
        self.assertEqual("external-not-checked", report["links"][0]["status"])

    def test_immutable_ref_does_not_read_mutated_working_source(self):
        self.assertEqual(1, len(self.inspect('[bad](absent.md)\n')["failures"]))
        self.assertEqual([], navigation.inspect(self.root, "HEAD")["failures"])

    def test_new_files_ignored_outputs_and_working_deletions(self):
        self.write(".gitignore", "ignored/\n")
        self.write("ignored/not-doc.md", '[bad](absent.md)\n')
        self.write("docs/new.md", '[valid](../asset.svg)\n')
        (self.root / "docs/target file.md").unlink()
        report = self.assertClean('[new](new.md)\n')
        self.assertEqual(3, report["markdownCount"])


if __name__ == "__main__":
    unittest.main()
