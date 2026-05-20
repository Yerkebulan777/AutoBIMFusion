using AutoBIMFusion.Plugin;
using AutoBIMFusion.Plugin.Commands;
using Autodesk.AutoCAD.Runtime;

#if !CORECONSOLE_DIAGNOSTICS
[assembly: ExtensionApplication(typeof(AutoBIMFusionExtension))]
#endif
[assembly: CommandClass(typeof(CombineCommands))]
