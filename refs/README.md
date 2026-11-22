# refs: Civil 3D reference DLLs for building other releases

`build.bat` and `build_package.bat` build the plugin for every Civil 3D release whose DLLs they find. A release
installed on the PC is found automatically. To build a release that is **not** installed here (for example 2024 and
2025 on a PC that has only 2027), copy its six DLLs into a folder named after the release:

```
refs\2024\   refs\2025\   refs\2027\
  accoremgd.dll            from  C:\Program Files\Autodesk\AutoCAD 20xx\
  acdbmgd.dll              from  C:\Program Files\Autodesk\AutoCAD 20xx\
  acmgd.dll                from  C:\Program Files\Autodesk\AutoCAD 20xx\
  AecBaseMgd.dll           from  C:\Program Files\Autodesk\AutoCAD 20xx\ACA\
  AeccDbMgd.dll            from  C:\Program Files\Autodesk\AutoCAD 20xx\C3D\
  AeccPressurePipesMgd.dll from  C:\Program Files\Autodesk\AutoCAD 20xx\C3D\
```

They are only compiled against, never shipped. The folders are git-ignored: these are Autodesk files and must not be
committed or redistributed. Check `build_result.txt` / `package_build.txt`: each release reports the folder its DLLs
came from.
