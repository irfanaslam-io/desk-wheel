// wheel-ocr: reads the text in an image with Apple's Vision framework (on-device, offline).
//
//   wheel-ocr <image>       prints the text, one line per line of text, top to bottom
//   wheel-ocr --languages   prints the languages this Mac's Vision can read, one per line
//   wheel-ocr --remove-background <image> <output.png>
//                           keeps the subject, makes the background transparent (macOS 14+)
//
// Exit codes: 0 ok (text may be empty), 2 usage, 3 image unreadable, 4 recognition failed,
//             5 needs a newer macOS, 6 no subject found.
// Built by build-mac.sh into Wheel.app/Contents/MacOS/; Wheel runs it as a separate process.

import CoreImage
import Foundation
import ImageIO
import Vision

func fail(_ message: String, _ code: Int32) -> Never {
    FileHandle.standardError.write((message + "\n").data(using: .utf8)!)
    exit(code)
}

func makeRequest() -> VNRecognizeTextRequest {
    let request = VNRecognizeTextRequest()
    request.recognitionLevel = .accurate
    request.usesLanguageCorrection = true
    if #available(macOS 13.0, *) {
        request.automaticallyDetectsLanguage = true
    }
    // English and Arabic first; whatever this macOS version supports of them.
    let wanted = ["en-US", "ar-SA", "ar"]
    if let supported = try? request.supportedRecognitionLanguages() {
        let chosen = wanted.filter { supported.contains($0) }
        if !chosen.isEmpty {
            request.recognitionLanguages = chosen
        }
    }
    return request
}

func isArabic(_ s: String) -> Bool {
    var arabic = 0, letters = 0
    for scalar in s.unicodeScalars where CharacterSet.letters.contains(scalar) {
        letters += 1
        if (0x0600...0x06FF).contains(scalar.value) || (0x0750...0x077F).contains(scalar.value)
            || (0x08A0...0x08FF).contains(scalar.value) || (0xFB50...0xFEFF).contains(scalar.value) {
            arabic += 1
        }
    }
    return letters > 0 && arabic * 2 >= letters
}

func loadImage(_ path: String) -> CGImage {
    guard let source = CGImageSourceCreateWithURL(URL(fileURLWithPath: path) as CFURL, nil),
          let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
        fail("Couldn't read the image.", 3)
    }
    return image
}

@available(macOS 14.0, *)
func removeBackground(_ input: String, _ output: String) {
    let image = loadImage(input)
    let request = VNGenerateForegroundInstanceMaskRequest()
    let handler = VNImageRequestHandler(cgImage: image, options: [:])
    do {
        try handler.perform([request])
    } catch {
        fail("Couldn't find the subject: \(error.localizedDescription)", 4)
    }
    guard let result = request.results?.first, !result.allInstances.isEmpty else {
        fail("No clear subject (a person, animal or object) was found in the picture.", 6)
    }
    do {
        let masked = try result.generateMaskedImage(ofInstances: result.allInstances, from: handler, croppedToInstancesExtent: false)
        let picture = CIImage(cvPixelBuffer: masked)
        guard let sRGB = CGColorSpace(name: CGColorSpace.sRGB) else { fail("No sRGB colour space.", 4) }
        try CIContext().writePNGRepresentation(of: picture, to: URL(fileURLWithPath: output), format: .RGBA8, colorSpace: sRGB)
    } catch {
        fail("Couldn't save the cut-out: \(error.localizedDescription)", 4)
    }
}

let args = CommandLine.arguments
if args.count == 4 && args[1] == "--remove-background" {
    if #available(macOS 14.0, *) {
        removeBackground(args[2], args[3])
        exit(0)
    }
    fail("Removing backgrounds needs macOS 14 (Sonoma) or later.", 5)
}
guard args.count == 2 else { fail("usage: wheel-ocr <image> | --languages | --remove-background <image> <output.png>", 2) }

if args[1] == "--languages" {
    let langs = (try? makeRequest().supportedRecognitionLanguages()) ?? []
    print(langs.joined(separator: "\n"))
    exit(0)
}

let image = loadImage(args[1])

let request = makeRequest()
do {
    try VNImageRequestHandler(cgImage: image, options: [:]).perform([request])
} catch {
    fail("Text recognition failed: \(error.localizedDescription)", 4)
}

// Each observation is one line of text. Vision's y axis points up, so higher midY = nearer the top.
struct Line { let text: String; let box: CGRect }
let lines: [Line] = (request.results ?? []).compactMap { obs in
    guard let best = obs.topCandidates(1).first else { return nil }
    return Line(text: best.string, box: obs.boundingBox)
}

// Group lines that sit on the same row (columns, table cells), then read rows top to bottom.
// Within a row, Arabic reads right to left.
var rows: [[Line]] = []
for line in lines.sorted(by: { $0.box.midY > $1.box.midY }) {
    if let last = rows.last?.first, abs(last.box.midY - line.box.midY) < min(last.box.height, line.box.height) / 2 {
        rows[rows.count - 1].append(line)
    } else {
        rows.append([line])
    }
}

var output: [String] = []
for row in rows {
    let rtl = isArabic(row.map(\.text).joined())
    let ordered = row.sorted { rtl ? $0.box.minX > $1.box.minX : $0.box.minX < $1.box.minX }
    output.append(ordered.map(\.text).joined(separator: rtl ? "  " : "    "))
}
print(output.joined(separator: "\n"))
