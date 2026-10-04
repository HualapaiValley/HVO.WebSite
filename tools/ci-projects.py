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
        project = {"path": path, "references": [], "inputs": [], "test": False}

        def visit(element, conditional=False):
            tag = element.tag.split("}")[-1]
            conditional = conditional or "Condition" in element.attrib
            if tag in ("Import", "Target", "Choose"):
                unsupported.append(f"{path}: explicit {tag} evaluation")
            if tag == "IsTestProject":
                value = (element.text or "").strip().lower()
                if conditional or value not in ("true", "false"):
                    unsupported.append(f"{path}: conditional/dynamic test identity")
                project["test"] = value == "true"
            if tag in ("ProjectReference", "Content", "None", "Compile", "AdditionalFiles", "EmbeddedResource"):
                item = element.get("Include") or element.get("Update")
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
                    except ValueError as error:
                        unsupported.append(f"{path}: {error}")
            for child in element:
                visit(child, conditional)

        visit(root)
        project["references"] = sorted(set(project["references"]))
        project["inputs"] = sorted(set(project["inputs"]))
        projects.append(project)
    for project in projects:
        for reference in project["references"]:
            if reference not in paths:
                unsupported.append(f"{project['path']}: reference outside active solution: {reference}")
    return {"projects": projects, "unsupported": sorted(set(unsupported))}


if __name__ == "__main__":
    json.dump(read_projects(json.load(sys.stdin)), sys.stdout, sort_keys=True)
