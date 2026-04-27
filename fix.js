const fs = require('fs');
const path = 'AutoBIMFusion/Application/Merge/Layouts/ViewportLayoutExporter.cs';
let content = fs.readFileSync(path, 'utf8');

content = content.replace(/LayoutViewportInfo mainClamped = ViewportScaleUtil\.ClampMainVpScale\(mainOriginal, log\);\r?\n\s*double clampRatio = ViewportScaleUtil\.ClampRatio\(mainOriginal, mainClamped\);\r?\n\r?\n\s*log\.Info\(\r?\n\s*\$\"VP main#\{mainOriginal\.Number\}: исходный scale=\{mainOriginal\.CustomScale:F6\}, \" \+\r?\n\s*\$\"рабочий scale=\{mainClamped\.CustomScale:F6\}, clampRatio=\{clampRatio:F6\}, \" \+\r?\n\s*\$\"центр=\{GeometryUtils\.FormatPoint\(mainOriginal\.ViewCenter\)\}\"\);/,
    log.Info(\n            $"VP main#{mainOriginal.Number}: scale={mainOriginal.CustomScale:F6}, " +\n            $"центр={GeometryUtils.FormatPoint(mainOriginal.ViewCenter)}"););

content = content.replace(/ViewportScaleUtil\.ApplyClampToModelSpace\(db, mainOriginal, clampRatio, log\);\r?\n\r?\n\s*return MovePaperToModelSpace\(db, layoutName, ViewportTransformer\.BuildPaperToMainMatrix\(mainClamped, log\), log\);/,
    eturn MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(mainOriginal, log), log););

content = content.replace(/private static \(Extents3d\? Bounds, HashSet<ObjectId> PaperClonedIds\) ProcessSingleVp\(Database db, string layoutName, LayoutViewportInfo vp, OperationLogger log\)\r?\n\s*\{\r?\n\s*log\.Info\(\$\"Выбранный метод масштабирования: ProcessSingleVp \(VP #\{vp\.Number\}\)\"\);\r?\n\r?\n\s*LayoutViewportInfo clamped = ViewportScaleUtil\.ClampMainVpScale\(vp, log\);\r?\n\s*double clampRatio = ViewportScaleUtil\.ClampRatio\(vp, clamped\);\r?\n\r?\n\s*log\.Info\(\r?\n\s*\$\"VP #\{vp\.Number\}: исходный scale=\{vp\.CustomScale:F6\}, рабочий scale=\{clamped\.CustomScale:F6\}, \" \+\r?\n\s*\$\"clampRatio=\{clampRatio:F6\}, центр=\{GeometryUtils\.FormatPoint\(clamped\.ViewCenter\)\}\"\);\r?\n\r?\n\s*ViewportScaleUtil\.ApplyClampToModelSpace\(db, vp, clampRatio, log\);\r?\n\r?\n\s*return MovePaperToModelSpace\(db, layoutName, ViewportTransformer\.BuildPaperToMainMatrix\(clamped, log\), log\);\r?\n\s*\}/,
    private static (Extents3d? Bounds, HashSet<ObjectId> PaperClonedIds) ProcessSingleVp(Database db, string layoutName, LayoutViewportInfo vp, OperationLogger log)\n    {\n        log.Info($"Выбранный метод масштабирования: ProcessSingleVp (VP #{vp.Number})");\n\n        log.Info(\n            $"VP #{vp.Number}: scale={vp.CustomScale:F6}, " +\n            $"центр={GeometryUtils.FormatPoint(vp.ViewCenter)}");\n\n        return MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(vp, log), log);\n    });

content = content.replace('log.Info($"Выбранный метод масштабирования: ProcessNoVp (масштаб по умолчанию 1:{ViewportScaleUtil.MaxScaleMultiplier:F0})");', 'log.Info($"Выбранный метод масштабирования: ProcessNoVp (масштаб по умолчанию 1:1)");');
content = content.replace('Matrix3d scale = Matrix3d.Scaling(ViewportScaleUtil.MaxScaleMultiplier, Point3d.Origin);', 'Matrix3d scale = Matrix3d.Scaling(1.0, Point3d.Origin);');
content = content.replace('$"ratio={ViewportScaleUtil.MaxScaleMultiplier:F2}");', '$"ratio=1.00");');

fs.writeFileSync(path, content, 'utf8');
console.log('Modified ViewportLayoutExporter.cs');
