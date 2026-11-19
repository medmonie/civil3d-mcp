// Project-wide usings. WinForms/System.Drawing implicit usings are removed in the .csproj so that names such as
// Application, Color or Region never become ambiguous with the AutoCAD ones.
global using System.Text.Json;
global using Autodesk.AutoCAD.ApplicationServices;
global using Autodesk.AutoCAD.DatabaseServices;
global using Autodesk.AutoCAD.Geometry;
global using static C3dMcpBridge.Db;
global using static C3dMcpBridge.Find;
global using static C3dMcpBridge.J;
global using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
global using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
global using CivilApp = Autodesk.Civil.ApplicationServices.CivilApplication;
global using CivilDoc = Autodesk.Civil.ApplicationServices.CivilDocument;
// NOTE: never add "using Autodesk.Civil.DatabaseServices;" - Surface, Entity and others exist in both the AutoCAD
// and the Civil namespaces. Always write CivilDb.Xxx / CivilStyles.Xxx.
global using CivilDb = Autodesk.Civil.DatabaseServices;
global using CivilStyles = Autodesk.Civil.DatabaseServices.Styles;
