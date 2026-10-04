import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("projects", Path(__file__).with_name("ci-projects.py"))
projects = importlib.util.module_from_spec(spec)
spec.loader.exec_module(projects)


class ProjectTests(unittest.TestCase):
    def graph(self, xml, second="<Project/>"):
        return projects.read_projects({
            'HVO.WebSite.sln': 'Project("id") = "App", "src\\App\\App.csproj", "a"\nProject("id") = "Lib", "src\\Lib\\Lib.csproj", "b"',
            'src/App/App.csproj': xml, 'src/Lib/Lib.csproj': second
        })

    def test_literal_references_and_copied_external_inputs(self):
        graph = self.graph('<Project><ItemGroup><ProjectReference Include="../Lib/Lib.csproj"/><None Include="../../docs/*.md" CopyToOutputDirectory="PreserveNewest"/></ItemGroup></Project>')
        self.assertEqual(graph['unsupported'], [])
        self.assertEqual(graph['projects'][0]['references'], ['src/Lib/Lib.csproj'])
        self.assertEqual(graph['projects'][0]['inputs'], ['docs/*.md'])

    def test_unsupported_evaluation_cannot_silently_remove_dependencies(self):
        for xml in (
            '<Project><Import Project="custom.props"/></Project>',
            '<Project><ItemGroup Condition="condition"><ProjectReference Include="../Lib/Lib.csproj"/></ItemGroup></Project>',
            '<Project><ItemGroup><ProjectReference Include="$(HiddenReference)"/></ItemGroup></Project>',
            '<Project><ItemGroup><ProjectReference Include="../../Elsewhere/Elsewhere.csproj"/></ItemGroup></Project>'
        ):
            self.assertTrue(self.graph(xml)['unsupported'])

    def test_unsafe_missing_or_malformed_project_data_fails(self):
        for xml in ('<!DOCTYPE Project [<!ENTITY data "x">]><Project/>', '<broken', 'x' * 131073):
            with self.assertRaises(Exception):
                self.graph(xml)
        with self.assertRaises(ValueError):
            projects.read_projects({'HVO.WebSite.sln': 'Project("id") = "Missing", "Missing.csproj", "a"'})
