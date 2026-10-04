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
            '<Project><ItemGroup Condition="condition"><ProjectReference Include="../Lib/Lib.csproj"/></ItemGroup></Project>',
            '<Project><ItemGroup><ProjectReference Include="$(HiddenReference)"/></ItemGroup></Project>',
            '<Project><ItemGroup><ProjectReference Include="../../Elsewhere/Elsewhere.csproj"/></ItemGroup></Project>'
        ):
            self.assertTrue(self.graph(xml)['unsupported'])

    def test_sdk_package_identity_is_preserved_without_redundant_property(self):
        for package in ('Microsoft.NET.Test.Sdk', 'MSTest.TestFramework', 'MSTest'):
            with self.subTest(package=package):
                graph = self.graph(f'<Project><ItemGroup><PackageReference Include="{package}"/></ItemGroup></Project>')
                self.assertTrue(graph['projects'][0]['test'])
        for metadata in (
            '<ItemGroup Condition="condition"><PackageReference Include="Microsoft.NET.Test.Sdk"/></ItemGroup>',
            '<PropertyGroup><IsTestProject>false</IsTestProject></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk"/></ItemGroup>',
            '<Import Project="hidden-test-identity.props"/>',
        ):
            with self.subTest(metadata=metadata), self.assertRaisesRegex(ValueError, 'test identity|test SDK identity'):
                self.graph('<Project>' + metadata + '</Project>')

    def test_unsafe_missing_or_malformed_project_data_fails(self):
        for xml in ('<!DOCTYPE Project [<!ENTITY data "x">]><Project/>', '<broken', 'x' * 131073):
            with self.assertRaises(Exception):
                self.graph(xml)
        with self.assertRaises(ValueError):
            projects.read_projects({'HVO.WebSite.sln': 'Project("id") = "Missing", "Missing.csproj", "a"'})

    def test_literal_compile_inputs_are_available_for_test_discovery(self):
        graph = self.graph('<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><Compile Include="../../shared/**/*.cs"><Link>Shared.cs</Link></Compile></ItemGroup></Project>')
        self.assertEqual(graph['projects'][0]['compileInputs'], ['shared/**/*.cs'])

    def test_unsupported_test_source_evaluation_fails_instead_of_under_discovering(self):
        for metadata in (
            '<ItemGroup><Compile Include="$(Sources)"/></ItemGroup>',
            '<ItemGroup><Compile Include="../../shared/*.cs" Exclude="../../shared/Fixture.cs"/></ItemGroup>',
            '<ItemGroup><Compile Remove="Fixture.cs"/></ItemGroup>',
            '<ItemGroup Condition="condition"><Compile Include="../../shared/*.cs"/></ItemGroup>',
            '<PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>',
            '<Import Project="shared.props"/>',
            '<Target Name="GenerateTests"/>',
        ):
            with self.subTest(metadata=metadata), self.assertRaisesRegex(ValueError, 'unsupported test source selection'):
                self.graph('<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>' + metadata + '</Project>')
        with self.assertRaisesRegex(ValueError, 'test identity'):
            self.graph('<Project><PropertyGroup><IsTestProject Condition="condition">true</IsTestProject></PropertyGroup></Project>')

    def test_implicit_build_imports_cannot_hide_test_sources(self):
        for metadata in ('<Compile Include="shared/*.cs"/>', '<Import Project="shared.props"/>', '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>'):
            with self.subTest(metadata=metadata), self.assertRaisesRegex(ValueError, 'shared test source selection'):
                projects.read_projects({
                    'HVO.WebSite.sln': 'Project("id") = "Tests", "tests/Tests.csproj", "a"',
                    'tests/Tests.csproj': '<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>',
                    'Directory.Build.props': '<Project>' + metadata + '</Project>'
                })
