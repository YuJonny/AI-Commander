import Foundation

// Generate a NATIVE CFURL bookmark for a file and write the raw bytes out.
// Native bookmarks resolve on modern macOS where Python mac_alias records do not.
let args = CommandLine.arguments
guard args.count >= 3 else {
    FileHandle.standardError.write("usage: genbookmark <file> <out.bin>\n".data(using: .utf8)!)
    exit(2)
}
let src = URL(fileURLWithPath: args[1])
let out = URL(fileURLWithPath: args[2])
do {
    let data = try src.bookmarkData(options: [],
                                    includingResourceValuesForKeys: nil,
                                    relativeTo: nil)
    try data.write(to: out)
    print("wrote \(data.count) bytes")
} catch {
    FileHandle.standardError.write("ERROR: \(error)\n".data(using: .utf8)!)
    exit(1)
}
