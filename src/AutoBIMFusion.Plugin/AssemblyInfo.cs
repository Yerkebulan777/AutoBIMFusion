using AutoBIMFusion.Plugin;
using AutoBIMFusion.Plugin.Commands;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(AutoBIMFusionExtension))]
[assembly: CommandClass(typeof(CombineCommands))]
