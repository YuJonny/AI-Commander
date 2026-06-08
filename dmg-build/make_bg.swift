import AppKit
import CoreGraphics

// DMG background, 660 x 440 window. Outputs bg.png (1x) + bg@2x.png (2x).
let width = 660
let height = 440

func drawBackground(scale: CGFloat) -> NSBitmapImageRep {
    let pixelW = Int(CGFloat(width) * scale)
    let pixelH = Int(CGFloat(height) * scale)
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil,
        pixelsWide: pixelW, pixelsHigh: pixelH,
        bitsPerSample: 8, samplesPerPixel: 4,
        hasAlpha: true, isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: pixelW * 4, bitsPerPixel: 32)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let ctx = NSGraphicsContext.current!.cgContext
    ctx.scaleBy(x: scale, y: scale)
    ctx.interpolationQuality = .high

    let gradient = NSGradient(colors: [
        NSColor(red: 0.985, green: 0.985, blue: 0.99, alpha: 1.0),
        NSColor(red: 0.93, green: 0.93, blue: 0.95, alpha: 1.0)
    ])!
    gradient.draw(in: NSRect(x: 0, y: 0, width: width, height: height), angle: -90)

    // Title
    let title = "AI 接线员"
    let titleAttrs: [NSAttributedString.Key: Any] = [
        .font: NSFont.systemFont(ofSize: 32, weight: .bold),
        .foregroundColor: NSColor(white: 0.1, alpha: 1.0),
        .kern: 2.0
    ]
    let titleStr = NSAttributedString(string: title, attributes: titleAttrs)
    let titleSize = titleStr.size()
    titleStr.draw(at: NSPoint(x: (CGFloat(width) - titleSize.width) / 2, y: CGFloat(height) - 60))

    // Commander subtitle
    let subtitle = "Commander"
    let subAttrs: [NSAttributedString.Key: Any] = [
        .font: NSFont.systemFont(ofSize: 13, weight: .medium),
        .foregroundColor: NSColor(white: 0.5, alpha: 1.0),
        .kern: 1.5
    ]
    let subStr = NSAttributedString(string: subtitle, attributes: subAttrs)
    let subSize = subStr.size()
    subStr.draw(at: NSPoint(x: (CGFloat(width) - subSize.width) / 2, y: CGFloat(height) - 88))

    // Instruction
    let instruction = "拖动 App 图标到 Applications 文件夹完成安装"
    let instrAttrs: [NSAttributedString.Key: Any] = [
        .font: NSFont.systemFont(ofSize: 13, weight: .regular),
        .foregroundColor: NSColor(white: 0.35, alpha: 1.0)
    ]
    let instrStr = NSAttributedString(string: instruction, attributes: instrAttrs)
    let instrSize = instrStr.size()
    instrStr.draw(at: NSPoint(x: (CGFloat(width) - instrSize.width) / 2, y: CGFloat(height) - 120))

    // Arrow between the two icons (icon row at Finder y=200 -> image y=240)
    let arrowY: CGFloat = 240
    let arrowStartX: CGFloat = 250
    let arrowEndX: CGFloat = 410
    let arrowColor = NSColor(red: 0.0, green: 0.48, blue: 1.0, alpha: 0.85)
    arrowColor.setStroke(); arrowColor.setFill()
    ctx.setLineWidth(3.5)
    ctx.setLineCap(.round)
    ctx.move(to: CGPoint(x: arrowStartX, y: arrowY))
    ctx.addLine(to: CGPoint(x: arrowEndX - 7, y: arrowY))
    ctx.strokePath()
    let head = CGMutablePath()
    head.move(to: CGPoint(x: arrowEndX + 10, y: arrowY))
    head.addLine(to: CGPoint(x: arrowEndX - 7, y: arrowY + 10))
    head.addLine(to: CGPoint(x: arrowEndX - 7, y: arrowY - 10))
    head.closeSubpath()
    ctx.addPath(head)
    ctx.fillPath()

    // Hint at bottom
    let hint = "② 首次打开会被系统拦截，属正常现象 — 按下方「打不开看这里.txt」操作即可，几步搞定"
    let hintAttrs: [NSAttributedString.Key: Any] = [
        .font: NSFont.systemFont(ofSize: 10, weight: .regular),
        .foregroundColor: NSColor(white: 0.55, alpha: 1.0)
    ]
    let hintStr = NSAttributedString(string: hint, attributes: hintAttrs)
    let hintSize = hintStr.size()
    hintStr.draw(at: NSPoint(x: (CGFloat(width) - hintSize.width) / 2, y: 22))

    NSGraphicsContext.restoreGraphicsState()
    return rep
}

func writePNG(_ rep: NSBitmapImageRep, _ path: String) {
    let data = rep.representation(using: .png, properties: [:])!
    try! data.write(to: URL(fileURLWithPath: path))
    print("wrote \(path) (\(rep.pixelsWide)x\(rep.pixelsHigh))")
}

let dir = (CommandLine.arguments.count > 1) ? CommandLine.arguments[1] : "."
writePNG(drawBackground(scale: 1), "\(dir)/bg.png")        // 660x440
writePNG(drawBackground(scale: 2), "\(dir)/bg@2x.png")     // 1320x880
