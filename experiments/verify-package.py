#!/usr/bin/env python3
"""Verify that the autoloader selects exactly one complete build for every supported host."""
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

bundle = Path(sys.argv[1])
root = ET.parse(bundle / "PackageContents.xml").getroot()
components = root.findall("Components")
for version in ("R23.1", "R24.0", "R24.1", "R24.2", "R24.3"):
    matching = [c for c in components if c.find("RuntimeRequirements").get("SeriesMin") <= version
                <= c.find("RuntimeRequirements").get("SeriesMax")]
    assert len(matching) == 1, (version, "expected one component", len(matching))
    requirements = matching[0].find("RuntimeRequirements")
    assert requirements.get("OS") == "Win64"
    assert requirements.get("Platform") == "AutoCAD*"
    dll = bundle / matching[0].find("ComponentEntry").get("ModuleName")
    assert {p.name for p in dll.parent.glob("*.dll")} == {"ACadHotKey.dll", "ACadHotKey.Core.dll"}, dll.parent
    for name in (dll.name, "ACadHotKey.Core.dll", "ACadHotKey.config.xml"):
        path = dll.parent / name
        assert path.is_file() and path.stat().st_size > 0, path
        if path.suffix == ".dll":
            assert path.read_bytes()[:2] == b"MZ", path
    ET.parse(dll.parent / "ACadHotKey.config.xml")
    print(f"{version}: complete package at {dll.parent}")
assert all(c.find("RuntimeRequirements").get("SeriesMax") < "R25.0" for c in components)
