"""Read literal project metadata as data; never evaluate candidate MSBuild code."""
import json
import posixpath
import re
import sys
import xml.etree.ElementTree as ET


def project_path(owner, value):
    if not value or any(character in value for character in "$@%;"):
        raise ValueError("dynamic or multiple item paths")
    value = posixpath.normpath(posixpath.join(posixpath.dirname(owner), value.replace("\\", "/")))
    if value.startswith(("../", "/")) or ":" in value:
        raise ValueError("item path outside repository")
    return value


def read_projects(files):
    solution = files.get("HVO.WebSite.sln", "")
    paths = sorted(set(path.replace("\\", "/") for path in re.findall(
        r'^Project\([^\n]+?\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)"', solution, re.MULTILINE)))
    if not paths:
        raise ValueError("active solution has no project membership")
    projects, unsupported = [], []
    for path in paths:
        if path not in files:
            raise ValueError(f"active project missing: {path}")
        source = files[path]
        if len(source) > 131072 or "<!DOCTYPE" in source or "<!ENTITY" in source:
            raise ValueError(f"unsafe/oversized project XML: {path}")
        root = ET.fromstring(source)
        sdk = root.get("Sdk", "Microsoft.NET.Sdk")
        if sdk not in ("Microsoft.NET.Sdk", "Microsoft.NET.Sdk.Web", "Microsoft.NET.Sdk.Razor"):
            raise ValueError(f"{path}: unsupported SDK test identity: {sdk}")
        project = {"path": path, "references": [], "inputs": [], "compileInputs": [], "test": False}
        test_source_errors = []
        explicit_test = None
        test_package = False

        def visit(element, conditional=False):
            nonlocal explicit_test, test_package
            tag = element.tag.split("}")[-1]
            conditional = conditional or "Condition" in element.attrib
            if tag in ("Import", "Sdk", "Target", "Choose"):
                raise ValueError(f"{path}: unsupported test source selection: explicit {tag} can change test identity")
            if tag == "PackageReference" and any(character in (element.get("Include") or element.get("Update") or "") for character in "$@%;"):
                raise ValueError(f"{path}: dynamic package test identity")
            if tag == "PackageReference" and (element.get("Include") or element.get("Update")) in ("Microsoft.NET.Test.Sdk", "MSTest.TestFramework", "MSTest"):
                if conditional or element.get("Remove") or element.get("Update"):
                    raise ValueError(f"{path}: conditional/modified test SDK identity")
                test_package = True
            if tag in ("EnableDefaultItems", "EnableDefaultCompileItems", "DefaultItemExcludes", "DefaultExcludesInProjectFolder", "OverrideDefaultCompileItems", "DirectoryBuildPropsPath", "DirectoryBuildTargetsPath"):
                test_source_errors.append(f"custom {tag} test source evaluation")
            if tag == "IsTestProject":
                value = (element.text or "").strip().lower()
                if conditional or value not in ("true", "false"):
                    raise ValueError(f"{path}: conditional/dynamic test identity")
                project["test"] = value == "true"
                explicit_test = project["test"]
            if tag in ("ProjectReference", "Content", "None", "Compile", "AdditionalFiles", "EmbeddedResource"):
                item = element.get("Include") or element.get("Update")
                if tag == "Compile" and (conditional or not element.get("Include") or any(key in element.attrib for key in ("Exclude", "Remove", "Update"))):
                    test_source_errors.append("conditional/modified Compile items")
                if tag == "ProjectReference" and (conditional or element.get("Remove") or not element.get("Include")):
                    unsupported.append(f"{path}: conditional/modified reference")
                if item:
                    try:
                        target = project_path(path, item)
                        if tag == "ProjectReference":
                            if any(character in target for character in "*?") or not target.endswith(".csproj"):
                                raise ValueError("nonliteral project reference")
                            project["references"].append(target)
                        else:
                            project["inputs"].append(target)
                            if tag == "Compile":
                                project["compileInputs"].append(target)
                    except ValueError as error:
                        unsupported.append(f"{path}: {error}")
                        if tag == "Compile":
                            test_source_errors.append(str(error))
            for child in element:
                visit(child, conditional)

        visit(root)
        if test_package and explicit_test is False:
            raise ValueError(f"{path}: conflicting explicit and package test identity")
        project["test"] = project["test"] or test_package
        if project["test"] and test_source_errors:
            raise ValueError(f"{path}: unsupported test source selection: {', '.join(sorted(set(test_source_errors)))}")
        project["references"] = sorted(set(project["references"]))
        project["inputs"] = sorted(set(project["inputs"]))
        project["compileInputs"] = sorted(set(project["compileInputs"]))
        projects.append(project)
    # Shared build files are implicit imports. They may set ordinary build/test
    # properties, but source selection there needs explicit reader support.
    for path, source in files.items():
        if posixpath.basename(path) not in ("Directory.Build.props", "Directory.Build.targets"):
            continue
        if len(source) > 131072 or "<!DOCTYPE" in source or "<!ENTITY" in source:
            raise ValueError(f"unsafe/oversized shared build XML: {path}")
        for element in ET.fromstring(source).iter():
            tag = element.tag.split("}")[-1]
            if tag in ("Compile", "Import", "Target", "Choose", "IsTestProject", "EnableDefaultItems", "EnableDefaultCompileItems", "DefaultItemExcludes", "DefaultExcludesInProjectFolder", "OverrideDefaultCompileItems", "DirectoryBuildPropsPath", "DirectoryBuildTargetsPath"):
                raise ValueError(f"{path}: unsupported shared test source selection: {tag}")
    for project in projects:
        for reference in project["references"]:
            if reference not in paths:
                unsupported.append(f"{project['path']}: reference outside active solution: {reference}")
    return {"projects": projects, "unsupported": sorted(set(unsupported))}


if __name__ == "__main__":
    json.dump(read_projects(json.load(sys.stdin)), sys.stdout, sort_keys=True)
