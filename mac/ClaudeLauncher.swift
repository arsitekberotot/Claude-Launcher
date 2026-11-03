import SwiftUI
import AppKit
import ApplicationServices
import Carbon.HIToolbox

// MARK: - Theme

extension Color {
    init(hex: String) {
        var s = hex.trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        let v = UInt32(s, radix: 16) ?? 0xD97757
        self.init(red: Double((v >> 16) & 0xFF) / 255, green: Double((v >> 8) & 0xFF) / 255, blue: Double(v & 0xFF) / 255)
    }

    // Monochrome, pure-black palette; white is the only accent.
    static let bg = Color(hex: "#000000")
    static let surface = Color(hex: "#111111")
    static let surfaceHover = Color(hex: "#1A1A1A")
    static let border = Color(hex: "#262626")
    static let borderHover = Color(hex: "#3A3A3A")
    static let text = Color(hex: "#FAFAFA")
    static let muted = Color(hex: "#8C8C8C")
    static let accent = Color(hex: "#FAFAFA")
    static let accentHover = Color(hex: "#E2E2E2")
    static let running = Color(hex: "#4ADE80")
    static let danger = Color(hex: "#F87171")
    static let canvas = Color(hex: "#0A0A0A")
    static let gridLine = Color(hex: "#161616")
    static let topBar = Color(hex: "#0A0A0A")
}

let palette: [(name: String, hex: String)] = [
    ("Oranye", "#D97757"), ("Biru", "#5B9BD5"), ("Hijau", "#62B480"), ("Ungu", "#AA84D6"),
    ("Kuning", "#D6A848"), ("Merah muda", "#D67096"), ("Toska", "#48ACA6"), ("Abu-abu", "#80868E")
]

func swatch(_ hex: String) -> NSImage {
    let image = NSImage(size: NSSize(width: 12, height: 12), flipped: false) { rect in
        NSColor(Color(hex: hex)).setFill()
        NSBezierPath(ovalIn: rect.insetBy(dx: 0.5, dy: 0.5)).fill()
        return true
    }
    return image
}

// MARK: - Paths & models

enum Paths {
    static let home = FileManager.default.homeDirectoryForCurrentUser
    static let root = home.appendingPathComponent(".claude-launcher")
    static let profiles = root.appendingPathComponent("profiles")
    static let defaultData = home.appendingPathComponent("Library/Application Support/Claude")
}

struct Profile: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var color: String
    var isTemplate: Bool = false

    static let defaultId = "default"
    var isDefault: Bool { id == Profile.defaultId }
    var dataDir: URL { isDefault ? Paths.defaultData : Paths.profiles.appendingPathComponent(id) }
    var subtitle: String { isDefault ? "Profil bawaan aplikasi" : "~/.claude-launcher/profiles/" + id }
}

struct ZoneLayout: Codable, Identifiable, Hashable {
    var id = UUID()
    var name: String
    var zones: [CGRect] // relative to the visible screen area, origin top-left, 0...1
}

struct LayoutSettings: Codable {
    var monitor: String = ""
    var layout: String = "grid"
    var gap: Bool = true
    var ids: [String]? = nil // nil = every profile

    func includes(_ p: Profile) -> Bool { ids == nil || ids!.contains(p.id) }
}

struct AppSettings: Codable {
    var autoStart = false
    var arrangeOnStart = false
    var closeToTray = true
    var hotkeys = true
}

struct Preset: Codable, Identifiable, Hashable {
    var id = UUID()
    var name: String
    var layout: String
    var monitor: String
    var gap: Bool
    var ids: [String]

    var label: String { "\(name)  (\(ids.count) instance)" }
}

// Global hotkeys via Carbon: no Accessibility permission needed. Ctrl+Option+1...9 and Ctrl+Option+0.
final class HotkeyCenter {
    static let shared = HotkeyCenter()
    var handler: ((Int) -> Void)?
    private var refs: [EventHotKeyRef] = []
    private var installed = false
    static let arrangeId = 0

    func register() {
        unregister()
        if !installed {
            var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
                var key = EventHotKeyID()
                GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                  nil, MemoryLayout<EventHotKeyID>.size, nil, &key)
                let id = Int(key.id)
                DispatchQueue.main.async { HotkeyCenter.shared.handler?(id) }
                return noErr
            }, 1, &spec, nil, nil)
            installed = true
        }
        let codes = [kVK_ANSI_0, kVK_ANSI_1, kVK_ANSI_2, kVK_ANSI_3, kVK_ANSI_4, kVK_ANSI_5, kVK_ANSI_6, kVK_ANSI_7, kVK_ANSI_8, kVK_ANSI_9]
        for (n, code) in codes.enumerated() {
            var ref: EventHotKeyRef?
            let id = EventHotKeyID(signature: OSType(0x434C_4C4E), id: UInt32(n)) // 'CLLN'
            if RegisterEventHotKey(UInt32(code), UInt32(controlKey | optionKey), id, GetApplicationEventTarget(), 0, &ref) == noErr,
               let ref = ref {
                refs.append(ref)
            }
        }
    }

    func unregister() {
        for r in refs { UnregisterEventHotKey(r) }
        refs = []
    }
}

struct AlertItem: Identifiable {
    let id = UUID()
    let title: String
    let message: String
}

enum Templates {
    static let keys = ["focus", "columns", "rows", "grid", "priority"]
    static let names = ["Focus", "Kolom", "Baris", "Grid", "Priority Grid"]
    static let customPrefix = "custom:"

    static func zones(_ key: String, _ count: Int) -> [CGRect] {
        let n = max(1, count)
        let f = CGFloat(n)
        switch key {
        case "columns":
            return (0..<n).map { CGRect(x: CGFloat($0) / f, y: 0, width: 1 / f, height: 1) }
        case "rows":
            return (0..<n).map { CGRect(x: 0, y: CGFloat($0) / f, width: 1, height: 1 / f) }
        case "focus":
            let step: CGFloat = n == 1 ? 0 : min(0.05, 0.3 / CGFloat(n - 1))
            return (0..<n).map { CGRect(x: 0.15 + CGFloat($0) * step, y: 0.1 + CGFloat($0) * step, width: 0.6, height: 0.65) }
        case "priority" where n >= 3:
            var z = [CGRect(x: 0.25, y: 0, width: 0.5, height: 1)]
            let rest = n - 1, left = (rest + 1) / 2, right = rest - left
            for i in 0..<left { z.append(CGRect(x: 0, y: CGFloat(i) / CGFloat(left), width: 0.25, height: 1 / CGFloat(left))) }
            for i in 0..<right { z.append(CGRect(x: 0.75, y: CGFloat(i) / CGFloat(right), width: 0.25, height: 1 / CGFloat(right))) }
            return z
        case "priority":
            return zones("columns", n)
        default: // grid: up to 3 side by side, then a square-ish grid whose last row stretches
            let cols = n <= 3 ? n : Int(ceil(sqrt(Double(n))))
            let rows = Int(ceil(Double(n) / Double(cols)))
            return (0..<n).map { i in
                let row = i / cols, col = i % cols
                let inRow = row == rows - 1 ? n - row * cols : cols
                return CGRect(x: CGFloat(col) / CGFloat(inRow), y: CGFloat(row) / CGFloat(rows),
                              width: 1 / CGFloat(inRow), height: 1 / CGFloat(rows))
            }
        }
    }

    static func resolve(_ key: String, _ count: Int, _ customs: [ZoneLayout]) -> [CGRect] {
        if key.hasPrefix(customPrefix) {
            let name = String(key.dropFirst(customPrefix.count))
            if let c = customs.first(where: { $0.name == name }) { return c.zones }
            return zones("grid", count)
        }
        return zones(keys.contains(key) ? key : "grid", count)
    }
}

// MARK: - Claude processes

enum Claude {
    static var appPath: String {
        NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.anthropic.claudefordesktop")?.path ?? "/Applications/Claude.app"
    }

    static func key(_ p: Profile) -> String {
        p.isDefault ? Profile.defaultId : p.dataDir.standardizedFileURL.path
    }

    @discardableResult
    static func run(_ tool: String, _ args: [String]) -> String {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = args
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        do { try process.run() } catch { return "" }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        return String(data: data, encoding: .utf8) ?? ""
    }

    // Profile key -> pid of its main Claude process (Electron helpers carry --type=).
    static func runningMains() -> [String: pid_t] {
        let binary = appPath + "/Contents/MacOS/Claude"
        var map: [String: pid_t] = [:]
        let regex = try! NSRegularExpression(pattern: "--user-data-dir=(.+?)(?= --|$)")
        for raw in run("/bin/ps", ["-axww", "-o", "pid=,command="]).split(separator: "\n") {
            let line = raw.trimmingCharacters(in: .whitespaces)
            guard let space = line.firstIndex(of: " "), let pid = pid_t(line[..<space]) else { continue }
            let cmd = String(line[line.index(after: space)...])
            guard cmd.hasPrefix(binary), !cmd.contains("--type=") else { continue }
            let ns = cmd as NSString
            if let m = regex.firstMatch(in: cmd, range: NSRange(location: 0, length: ns.length)) {
                let dir = ns.substring(with: m.range(at: 1)).trimmingCharacters(in: CharacterSet(charactersIn: "\"' "))
                map[URL(fileURLWithPath: dir).standardizedFileURL.path] = pid
            } else {
                map[Profile.defaultId] = pid
            }
        }
        return map
    }

    static func launch(_ p: Profile) {
        if p.isDefault {
            run("/usr/bin/open", ["-a", appPath])
        } else {
            try? FileManager.default.createDirectory(at: p.dataDir, withIntermediateDirectories: true)
            // A second process with the same data dir hands over to the running one, which shows its window.
            run("/usr/bin/open", ["-n", "-a", appPath, "--args", "--user-data-dir=" + p.dataDir.path])
        }
    }
}

// MARK: - Signed-in account

// Reads the signed-in email from Claude's cached claude.ai account data. Only the
// account uuid (config.json) and the cached account record are read; never tokens.
enum AccountInfo {
    private static var cache: [String: (stamp: Date, email: String?)] = [:]
    private static let lock = NSLock()

    static func email(_ dir: URL) -> String? {
        let fm = FileManager.default
        let config = dir.appendingPathComponent("config.json")
        let blob = dir.appendingPathComponent("IndexedDB/https_claude.ai_0.indexeddb.blob")
        let keys: [URLResourceKey] = [.contentModificationDateKey, .isRegularFileKey, .fileSizeKey]
        guard fm.fileExists(atPath: config.path),
              let walker = fm.enumerator(at: blob, includingPropertiesForKeys: keys) else { return nil }

        var files: [(url: URL, date: Date)] = []
        for case let url as URL in walker {
            guard let v = try? url.resourceValues(forKeys: Set(keys)), v.isRegularFile == true,
                  (v.fileSize ?? 0) < 50_000_000 else { continue }
            files.append((url, v.contentModificationDate ?? .distantPast))
        }
        files.sort { $0.date > $1.date }
        let configDate = (try? config.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate ?? .distantPast
        let stamp = max(configDate, files.first?.date ?? .distantPast)

        lock.lock()
        if let hit = cache[dir.path], hit.stamp == stamp { lock.unlock(); return hit.email }
        lock.unlock()

        var result: String?
        if let text = try? String(contentsOf: config, encoding: .utf8),
           let regex = try? NSRegularExpression(pattern: "\"lastKnownAccountUuid\"\\s*:\\s*\"([0-9a-fA-F-]{36})\""),
           let m = regex.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)),
           let r = Range(m.range(at: 1), in: text) {
            let uuid = String(text[r])
            for f in files {
                guard let data = try? Data(contentsOf: f.url, options: .mappedIfSafe) else { continue }
                if let e = findEmail(data, uuid) { result = e; break }
            }
        }
        lock.lock()
        cache[dir.path] = (stamp, result)
        lock.unlock()
        return result
    }

    // The cache is V8-serialized: "email_address" is followed by '"', a varint length, then the bytes.
    private static func findEmail(_ data: Data, _ uuid: String) -> String? {
        let u = Data(uuid.utf8), key = Data("email_address".utf8)
        var from = data.startIndex
        while let r = data.range(of: u, in: from..<data.endIndex) {
            from = r.upperBound
            guard let k = data.range(of: key, in: r.upperBound..<min(data.endIndex, r.upperBound + 400)) else { continue }
            var p = k.upperBound
            guard p < data.endIndex, data[p] == 0x22 else { continue }
            p += 1
            var length = 0, shift = 0
            while p < data.endIndex, shift < 28 {
                let b = Int(data[p])
                p += 1
                length |= (b & 0x7F) << shift
                if b & 0x80 == 0 { break }
                shift += 7
            }
            guard length > 0, length <= 254, p + length <= data.endIndex,
                  let s = String(data: data[p..<(p + length)], encoding: .isoLatin1),
                  s.range(of: "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", options: .regularExpression) != nil else { continue }
            return s
        }
        return nil
    }
}

// MARK: - Usage limit snapshot

struct UsageWindow {
    var utilization: Double? // 0...1
    var resetsAt: Date?
    var hasReset: Bool { resetsAt.map { $0 <= Date() } ?? false }
}

struct UsageSnapshot {
    var fiveHour: UsageWindow?
    var weekly: UsageWindow?
    var taken: Date
}

// Last rate-limit snapshot Claude cached with a conversation. It is NOT live: Claude fetches
// current usage from the server and does not store it, so this can be days old.
enum UsageInfo {
    private static var cache: [String: (stamp: Date, usage: UsageSnapshot?)] = [:]
    private static let lock = NSLock()

    static func latest(_ dir: URL) -> UsageSnapshot? {
        let blob = dir.appendingPathComponent("IndexedDB/https_claude.ai_0.indexeddb.blob")
        let keys: [URLResourceKey] = [.contentModificationDateKey, .isRegularFileKey, .fileSizeKey]
        guard let walker = FileManager.default.enumerator(at: blob, includingPropertiesForKeys: keys) else { return nil }
        var files: [(url: URL, date: Date)] = []
        for case let url as URL in walker {
            guard let v = try? url.resourceValues(forKeys: Set(keys)), v.isRegularFile == true,
                  (v.fileSize ?? 0) < 50_000_000 else { continue }
            files.append((url, v.contentModificationDate ?? .distantPast))
        }
        let stamp = files.map { $0.date }.max() ?? .distantPast
        lock.lock()
        if let hit = cache[dir.path], hit.stamp == stamp { lock.unlock(); return hit.usage }
        lock.unlock()

        var best: UsageSnapshot?
        for f in files {
            guard let data = try? Data(contentsOf: f.url), let raw = unwrap([UInt8](data)),
                  let found = parse(raw), found.taken > (best?.taken ?? .distantPast) else { continue }
            best = found
        }
        lock.lock()
        cache[dir.path] = (stamp, best)
        lock.unlock()
        return best
    }

    // Chromium IndexedDB values: 0xFF <version> then 0x02 = Snappy-compressed V8 data.
    private static func unwrap(_ d: [UInt8]) -> [UInt8]? {
        guard d.count > 4, d[0] == 0xFF else { return nil }
        return d[2] == 0x02 ? snappy(d, from: 3) : d
    }

    // Raw (unframed) Snappy block decoder.
    private static func snappy(_ src: [UInt8], from start: Int) -> [UInt8]? {
        var pos = start, length = 0, shift = 0
        while pos < src.count {
            let b = Int(src[pos]); pos += 1
            length |= (b & 0x7F) << shift
            if b & 0x80 == 0 { break }
            shift += 7
        }
        var dst = [UInt8](repeating: 0, count: length)
        var d = 0
        while pos < src.count && d < length {
            let tag = Int(src[pos]); pos += 1
            if tag & 3 == 0 {
                var n = tag >> 2
                if n >= 60 {
                    let bytes = n - 59
                    n = 0
                    for i in 0..<bytes { guard pos < src.count else { return nil }; n |= Int(src[pos]) << (8 * i); pos += 1 }
                }
                n += 1
                guard pos + n <= src.count, d + n <= length else { return nil }
                dst.replaceSubrange(d..<(d + n), with: src[pos..<(pos + n)])
                pos += n; d += n
            } else {
                var n = 0, offset = 0
                switch tag & 3 {
                case 1:
                    guard pos < src.count else { return nil }
                    n = ((tag >> 2) & 7) + 4; offset = ((tag >> 5) << 8) | Int(src[pos]); pos += 1
                case 2:
                    guard pos + 1 < src.count else { return nil }
                    n = (tag >> 2) + 1; offset = Int(src[pos]) | (Int(src[pos + 1]) << 8); pos += 2
                default:
                    guard pos + 3 < src.count else { return nil }
                    n = (tag >> 2) + 1
                    offset = Int(src[pos]) | Int(src[pos + 1]) << 8 | Int(src[pos + 2]) << 16 | Int(src[pos + 3]) << 24
                    pos += 4
                }
                guard offset > 0, offset <= d, d + n <= length else { return nil }
                for _ in 0..<n { dst[d] = dst[d - offset]; d += 1 }
            }
        }
        return dst
    }

    private static func find(_ needle: String, in hay: [UInt8], from: Int, to: Int) -> Int? {
        let n = Array(needle.utf8)
        guard !n.isEmpty, from >= 0, to <= hay.count, to - from >= n.count else { return nil }
        var i = from
        outer: while i <= to - n.count {
            for j in 0..<n.count where hay[i + j] != n[j] { i += 1; continue outer }
            return i
        }
        return nil
    }

    // rate_limit_info.unifiedWindows = { five_hour: { resetsAt, status, utilization }, seven_day: { ... } }
    private static func parse(_ raw: [UInt8]) -> UsageSnapshot? {
        var best: UsageSnapshot?
        var from = 0
        while let i = find("unifiedWindows", in: raw, from: from, to: raw.count) {
            from = i + 1
            let end = min(raw.count, i + 600)
            let five = window(raw, i, end, "five_hour"), week = window(raw, i, end, "seven_day")
            guard five != nil || week != nil, let taken = timestamp(raw, i) else { continue }
            if taken > (best?.taken ?? .distantPast) { best = UsageSnapshot(fiveHour: five, weekly: week, taken: taken) }
        }
        return best
    }

    // V8 object: key string, then the value ('o' starts an object, '{' ends it).
    private static func window(_ raw: [UInt8], _ from: Int, _ end: Int, _ name: String) -> UsageWindow? {
        guard let k = find(name + "o", in: raw, from: from, to: end) else { return nil }
        let objEnd = raw[k..<end].firstIndex(of: UInt8(ascii: "{")) ?? end
        var w = UsageWindow(utilization: number(raw, k, objEnd, "utilization"), resetsAt: nil)
        if let r = number(raw, k, objEnd, "resetsAt") { w.resetsAt = Date(timeIntervalSince1970: r > 1e12 ? r / 1000 : r) }
        if let u = w.utilization, u > 1 { w.utilization = u / 100 }
        return w.utilization != nil || w.resetsAt != nil ? w : nil
    }

    // 'N' = double (8 bytes LE), 'I' = zigzag varint int32.
    private static func number(_ raw: [UInt8], _ from: Int, _ end: Int, _ key: String) -> Double? {
        guard let k = find(key, in: raw, from: from, to: end) else { return nil }
        var p = k + key.utf8.count
        guard p < raw.count else { return nil }
        if raw[p] == UInt8(ascii: "N"), p + 9 <= raw.count {
            var bits: UInt64 = 0
            for i in 0..<8 { bits |= UInt64(raw[p + 1 + i]) << (8 * UInt64(i)) }
            return Double(bitPattern: bits)
        }
        if raw[p] == UInt8(ascii: "I") {
            var v = 0, shift = 0
            p += 1
            while p < raw.count, shift < 35 {
                v |= Int(raw[p] & 0x7F) << shift
                if raw[p] & 0x80 == 0 { break }
                p += 1; shift += 7
            }
            return Double((v >> 1) ^ -(v & 1))
        }
        return nil
    }

    private static let isoRegex = try! NSRegularExpression(pattern: "20\\d\\d-\\d\\d-\\d\\dT\\d\\d:\\d\\d:\\d\\d(\\.\\d+)?Z")

    private static func timestamp(_ raw: [UInt8], _ at: Int) -> Date? {
        func iso(_ lo: Int, _ hi: Int, last: Bool) -> Date? {
            let s = String(decoding: raw[max(0, lo)..<min(raw.count, hi)].map { $0 < 0x80 ? $0 : 0x2E }, as: UTF8.self)
            let ms = isoRegex.matches(in: s, range: NSRange(s.startIndex..., in: s))
            guard let m = last ? ms.last : ms.first, let r = Range(m.range, in: s) else { return nil }
            let f = ISO8601DateFormatter()
            f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            let text = String(s[r])
            return f.date(from: text) ?? ISO8601DateFormatter().date(from: text.replacingOccurrences(of: "\\.\\d+", with: "", options: .regularExpression))
        }
        return iso(at, at + 900, last: false) ?? iso(at - 600, at, last: true)
    }
}

// MARK: - Setup copy

struct CopyOptions {
    var mcp = true, prefs = true, toolToggles = true, presets = true, extensions = true
}

enum SetupCopier {
    static let desktopConfig = "claude_desktop_config.json"
    static let extensionDirs = ["Claude Extensions", "Claude Extensions Settings"]
    // Per-machine session/sidebar state that should not follow a profile.
    static let skipPrefKeys: Set<String> = ["epitaxyPrefs", "launchPreviewPersistedWorkspaces", "launchPreviewSessionScopedSessions"]

    // Login (config.json, cookies) is never copied.
    static func copy(from: Profile, to: Profile, options o: CopyOptions) throws -> String {
        let fm = FileManager.default
        let src = from.dataDir, dst = to.dataDir
        guard fm.fileExists(atPath: src.path) else {
            throw NSError(domain: "launcher", code: 1, userInfo: [NSLocalizedDescriptionKey: "Folder data \"\(from.name)\" belum ada. Buka instance itu sekali dulu."])
        }
        try fm.createDirectory(at: dst, withIntermediateDirectories: true)
        let stamp = DateFormatter()
        stamp.dateFormat = "yyyyMMdd-HHmmss"
        let backup = dst.appendingPathComponent("launcher-backup").appendingPathComponent(stamp.string(from: Date()))
        var done: [String] = []

        func backupItem(_ url: URL) throws {
            guard fm.fileExists(atPath: url.path) else { return }
            try fm.createDirectory(at: backup, withIntermediateDirectories: true)
            let target = backup.appendingPathComponent(url.lastPathComponent)
            if fm.fileExists(atPath: target.path) { try fm.removeItem(at: target) }
            try fm.copyItem(at: url, to: target)
        }

        func replace(_ name: String) throws -> Bool {
            let s = src.appendingPathComponent(name), d = dst.appendingPathComponent(name)
            guard fm.fileExists(atPath: s.path) else { return false }
            try backupItem(d)
            if fm.fileExists(atPath: d.path) { try fm.removeItem(at: d) }
            try fm.copyItem(at: s, to: d)
            return true
        }

        if o.mcp || o.prefs {
            let srcCfg = readJSON(src.appendingPathComponent(desktopConfig))
            let dstURL = dst.appendingPathComponent(desktopConfig)
            var dstCfg = readJSON(dstURL)
            if o.mcp {
                dstCfg["mcpServers"] = srcCfg["mcpServers"]
                done.append("MCP server (\((srcCfg["mcpServers"] as? [String: Any])?.count ?? 0))")
            }
            if o.prefs {
                for (k, v) in srcCfg where k != "mcpServers" && k != "preferences" { dstCfg[k] = v }
                var prefs = dstCfg["preferences"] as? [String: Any] ?? [:]
                for (k, v) in srcCfg["preferences"] as? [String: Any] ?? [:] where !skipPrefKeys.contains(k) { prefs[k] = v }
                dstCfg["preferences"] = prefs
                done.append("Preferensi aplikasi")
            }
            try backupItem(dstURL)
            let data = try JSONSerialization.data(withJSONObject: dstCfg, options: [.prettyPrinted, .sortedKeys])
            try data.write(to: dstURL)
        }
        if o.toolToggles, try replace("mcp-user-tool-toggles.json") { done.append("Status tool MCP") }
        if o.presets, try replace("ccd-session-presets.json") { done.append("Preset sesi Claude Code") }
        if o.extensions {
            var any = false
            for dir in extensionDirs where try replace(dir) { any = true }
            if any { done.append("Extension") }
        }

        if done.isEmpty { return "Tidak ada yang disalin. Sumber belum punya setup yang dipilih." }
        var summary = "Disalin: " + done.joined(separator: ", ") + "."
        if fm.fileExists(atPath: backup.path) { summary += "\n\nFile lama dibackup ke:\n" + backup.path }
        return summary
    }

    static func readJSON(_ url: URL) -> [String: Any] {
        guard let data = try? Data(contentsOf: url),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return [:] }
        return obj
    }
}

// MARK: - Monitors & window arranging (Accessibility API)

struct MonitorInfo: Identifiable, Hashable {
    let id: String
    let number: Int
    let name: String
    let frame: CGRect
    let visible: CGRect
    let scale: CGFloat
    let isMain: Bool
}

enum Arranger {
    static func monitors() -> [MonitorInfo] {
        let screens = NSScreen.screens
        guard let primary = screens.first else { return [] }
        let sorted = screens.sorted { a, b in
            if (a == primary) != (b == primary) { return a == primary }
            return a.frame.minX < b.frame.minX
        }
        return sorted.enumerated().map { i, s in
            let num = (s.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.stringValue ?? "\(i)"
            return MonitorInfo(id: num, number: i + 1, name: s.localizedName, frame: s.frame, visible: s.visibleFrame,
                               scale: s.backingScaleFactor, isMain: s == primary)
        }
    }

    static func trusted(prompt: Bool) -> Bool {
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        return AXIsProcessTrustedWithOptions([key: prompt] as CFDictionary)
    }

    static func attribute<T>(_ element: AXUIElement, _ name: String) -> T? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success else { return nil }
        return value as? T
    }

    static func size(_ window: AXUIElement) -> CGSize {
        guard let value: AXValue = attribute(window, kAXSizeAttribute) else { return .zero }
        var s = CGSize.zero
        AXValueGetValue(value, .cgSize, &s)
        return s
    }

    // Largest standard window of the process.
    static func mainWindow(pid: pid_t) -> AXUIElement? {
        let app = AXUIElementCreateApplication(pid)
        guard let windows: [AXUIElement] = attribute(app, kAXWindowsAttribute) else { return nil }
        return windows
            .filter { (attribute($0, kAXSubroleAttribute) as String?) == (kAXStandardWindowSubrole as String) }
            .max { size($0).width * size($0).height < size($1).width * size($1).height }
    }

    static func place(_ window: AXUIElement, _ rect: CGRect) {
        AXUIElementSetAttributeValue(window, kAXMinimizedAttribute as CFString, kCFBooleanFalse)
        var origin = rect.origin
        var size = rect.size
        let pos = AXValueCreate(.cgPoint, &origin)!
        let sz = AXValueCreate(.cgSize, &size)!
        // Position, size, position again: the first move may hop screens and clamp the size.
        AXUIElementSetAttributeValue(window, kAXPositionAttribute as CFString, pos)
        AXUIElementSetAttributeValue(window, kAXSizeAttribute as CFString, sz)
        AXUIElementSetAttributeValue(window, kAXPositionAttribute as CFString, pos)
    }

    // Window i goes to zone i; extra windows wrap around to the first zones.
    static func arrange(_ windows: [AXUIElement], on monitor: MonitorInfo, zones: [CGRect], gap: Bool) {
        guard !windows.isEmpty, !zones.isEmpty, let primary = NSScreen.screens.first else { return }
        let g: CGFloat = gap ? 8 : 0
        // Cocoa uses a bottom-left origin; Accessibility uses top-left of the primary screen.
        let v = monitor.visible
        let area = CGRect(x: v.minX + g / 2, y: primary.frame.height - v.maxY + g / 2, width: v.width - g, height: v.height - g)
        for (i, w) in windows.enumerated() {
            let z = zones[i % zones.count]
            let x1 = (area.minX + z.minX * area.width).rounded(), x2 = (area.minX + z.maxX * area.width).rounded()
            let y1 = (area.minY + z.minY * area.height).rounded(), y2 = (area.minY + z.maxY * area.height).rounded()
            place(w, CGRect(x: x1 + g / 2, y: y1 + g / 2, width: x2 - x1 - g, height: y2 - y1 - g))
        }
    }
}

// MARK: - App model

@MainActor
final class AppModel: ObservableObject {
    @Published var profiles: [Profile] = []
    @Published var running: [String: pid_t] = [:] // profile id -> pid
    @Published var emails: [String: String] = [:] // profile id -> signed-in email
    @Published var usages: [String: UsageSnapshot] = [:] // profile id -> last cached limit snapshot
    @Published var layout = LayoutSettings()
    @Published var customs: [ZoneLayout] = []
    @Published var arrangeStatus: String?
    @Published var alert: AlertItem?
    @Published var settings = AppSettings()
    @Published var presets: [Preset] = []
    private var timer: Timer?

    // Read by the app delegate: keep running in the menu bar after the window closes.
    static var keepRunning = true
    let autostarted = CommandLine.arguments.contains("--autostart")

    private let profilesURL = Paths.root.appendingPathComponent("profiles.json")
    private let layoutURL = Paths.root.appendingPathComponent("layout.json")
    private let customsURL = Paths.root.appendingPathComponent("layouts.json")
    private let settingsURL = Paths.root.appendingPathComponent("settings.json")
    private let presetsURL = Paths.root.appendingPathComponent("presets.json")
    private static let agentLabel = "com.hariz.claudelauncher"
    private static let agentURL = Paths.home.appendingPathComponent("Library/LaunchAgents/\(agentLabel).plist")

    init() {
        load()
        refresh()
        timer = Timer.scheduledTimer(withTimeInterval: 2.5, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.refresh() }
        }
        HotkeyCenter.shared.handler = { [weak self] id in
            guard let self = self else { return }
            if id == HotkeyCenter.arrangeId { Task { await self.openAndArrange() } }
            else if id >= 1 && id <= self.profiles.count { self.launch(self.profiles[id - 1]) }
        }
        if settings.hotkeys { HotkeyCenter.shared.register() }
        if autostarted && settings.arrangeOnStart {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4) { Task { await self.openAndArrange() } }
        }
    }

    // MARK: settings, presets, closing

    func update(settings change: (inout AppSettings) -> Void) {
        change(&settings)
        AppModel.keepRunning = settings.closeToTray
        write(settings, settingsURL)
    }

    func setAutoStart(_ on: Bool) {
        do {
            if on {
                let plist: [String: Any] = [
                    "Label": AppModel.agentLabel,
                    "ProgramArguments": [Bundle.main.executablePath ?? "", "--autostart"],
                    "RunAtLoad": true
                ]
                let data = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
                try FileManager.default.createDirectory(at: AppModel.agentURL.deletingLastPathComponent(), withIntermediateDirectories: true)
                try data.write(to: AppModel.agentURL)
            } else if FileManager.default.fileExists(atPath: AppModel.agentURL.path) {
                try FileManager.default.removeItem(at: AppModel.agentURL)
            }
            update { $0.autoStart = on }
        } catch {
            alert = AlertItem(title: "Gagal mengubah", message: error.localizedDescription)
        }
    }

    func setHotkeys(_ on: Bool) {
        update { $0.hotkeys = on }
        if on { HotkeyCenter.shared.register() } else { HotkeyCenter.shared.unregister() }
    }

    func confirm(_ title: String, _ message: String, _ ok: String) -> Bool {
        let a = NSAlert()
        a.messageText = title
        a.informativeText = message
        a.alertStyle = .warning
        a.addButton(withTitle: ok)
        a.addButton(withTitle: "Batal")
        NSApp.activate(ignoringOtherApps: true)
        return a.runModal() == .alertFirstButtonReturn
    }

    func closeInstance(_ p: Profile) {
        guard let pid = running[p.id] else { return }
        guard confirm("Tutup instance", "Tutup \"\(p.name)\"? Claude di instance ini akan keluar.", "Tutup") else { return }
        NSRunningApplication(processIdentifier: pid)?.terminate()
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { self.refresh() }
    }

    func closeAll() {
        let targets = profiles.filter { running[$0.id] != nil }
        guard !targets.isEmpty else {
            alert = AlertItem(title: "Tidak ada yang berjalan", message: "Belum ada instance yang sedang berjalan.")
            return
        }
        let names = targets.map { "\u{2022} " + $0.name }.joined(separator: "\n")
        guard confirm("Tutup semua instance", "Tutup \(targets.count) instance yang sedang berjalan?\n\n" + names, "Tutup semua") else { return }
        for t in targets { if let pid = running[t.id] { NSRunningApplication(processIdentifier: pid)?.terminate() } }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { self.refresh() }
    }

    func savePreset(name: String) {
        let active = profiles.filter { layout.includes($0) }.map { $0.id }
        presets.removeAll { $0.name.lowercased() == name.lowercased() }
        presets.append(Preset(name: name, layout: layout.layout, monitor: layout.monitor, gap: layout.gap, ids: active))
        write(presets, presetsURL)
    }

    func deletePreset(_ p: Preset) {
        guard confirm("Hapus preset", "Hapus preset \"\(p.name)\"?", "Hapus") else { return }
        presets.removeAll { $0.id == p.id }
        write(presets, presetsURL)
    }

    func runPreset(_ p: Preset) async {
        layout.layout = p.layout
        layout.monitor = p.monitor
        layout.gap = p.gap
        layout.ids = profiles.map { $0.id }.filter { p.ids.contains($0) }
        saveLayout()
        await openAndArrange()
    }

    func hotkeyHelp() {
        var lines = ["\u{2303}\u{2325}0  \u{2192}  Buka & rapikan"]
        for (i, p) in profiles.prefix(9).enumerated() { lines.append("\u{2303}\u{2325}\(i + 1)  \u{2192}  \(p.name)") }
        lines.append("")
        lines.append(settings.hotkeys ? "Hotkey sedang aktif. Nomor mengikuti urutan daftar." : "Hotkey sedang nonaktif. Aktifkan dari menu \u{2261}.")
        alert = AlertItem(title: "Hotkey global", message: lines.joined(separator: "\n"))
    }

    // MARK: persistence

    private func load() {
        let decoder = JSONDecoder()
        if let data = try? Data(contentsOf: profilesURL), let list = try? decoder.decode([Profile].self, from: data), !list.isEmpty {
            profiles = list
        } else {
            profiles = [Profile(id: Profile.defaultId, name: "Utama", color: palette[0].hex)]
            saveProfiles()
        }
        if let data = try? Data(contentsOf: layoutURL), let s = try? decoder.decode(LayoutSettings.self, from: data) { layout = s }
        if let data = try? Data(contentsOf: customsURL), let c = try? decoder.decode([ZoneLayout].self, from: data) { customs = c }
        if let data = try? Data(contentsOf: settingsURL), let s = try? decoder.decode(AppSettings.self, from: data) { settings = s }
        if let data = try? Data(contentsOf: presetsURL), let p = try? decoder.decode([Preset].self, from: data) { presets = p }
        AppModel.keepRunning = settings.closeToTray
    }

    private func write<T: Encodable>(_ value: T, _ url: URL) {
        try? FileManager.default.createDirectory(at: Paths.root, withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted]
        if let data = try? encoder.encode(value) { try? data.write(to: url) }
    }

    func saveProfiles() { write(profiles, profilesURL) }
    func saveLayout() { write(layout, layoutURL) }
    func saveCustoms() { write(customs, customsURL) }

    // MARK: status

    func refresh() {
        let snapshot = profiles
        Task.detached {
            let procs = Claude.runningMains()
            var map: [String: pid_t] = [:]
            var mail: [String: String] = [:]
            var usage: [String: UsageSnapshot] = [:]
            for p in snapshot {
                if let pid = procs[Claude.key(p)] { map[p.id] = pid }
                if let e = AccountInfo.email(p.dataDir) { mail[p.id] = e }
                if let u = UsageInfo.latest(p.dataDir) { usage[p.id] = u }
            }
            let result = map, emails = mail, usages = usage
            await MainActor.run {
                self.running = result
                self.emails = emails
                self.usages = usages
            }
        }
    }

    func isRunning(_ p: Profile) -> Bool { running[p.id] != nil }

    var activeCount: Int { profiles.filter { layout.includes($0) }.count }

    // The card switch and the layout editor's instance checks share layout.ids.
    func setActive(_ p: Profile, _ active: Bool) {
        var ids = Set(layout.ids ?? profiles.map { $0.id })
        if active { ids.insert(p.id) } else { ids.remove(p.id) }
        layout.ids = profiles.map { $0.id }.filter { ids.contains($0) }
        saveLayout()
    }

    // MARK: profiles

    func newId(for name: String) -> String {
        let slug = name.lowercased().replacingOccurrences(of: "[^a-z0-9]+", with: "-", options: .regularExpression)
            .trimmingCharacters(in: CharacterSet(charactersIn: "-"))
        let base = slug.isEmpty ? "instance" : slug
        var id = base, n = 2
        while id == Profile.defaultId || profiles.contains(where: { $0.id == id }) { id = "\(base)-\(n)"; n += 1 }
        return id
    }

    static func validateName(_ name: String) -> String? {
        if name.isEmpty { return "Nama tidak boleh kosong." }
        if name.count > 40 { return "Maksimal 40 karakter." }
        return nil
    }

    func add(name: String) {
        let profile = Profile(id: newId(for: name), name: name, color: palette[profiles.count % palette.count].hex)
        profiles.append(profile)
        saveProfiles()
        if let template = profiles.first(where: { $0.isTemplate }) {
            do { _ = try SetupCopier.copy(from: template, to: profile, options: CopyOptions()) }
            catch { alert = AlertItem(title: "Template gagal disalin", message: "Instance \"\(name)\" sudah dibuat, tapi setup dari template gagal disalin.\n\n\(error.localizedDescription)") }
        }
    }

    func update(_ p: Profile, _ change: (inout Profile) -> Void) {
        guard let i = profiles.firstIndex(of: p) else { return }
        change(&profiles[i])
        saveProfiles()
    }

    func toggleTemplate(_ p: Profile) {
        let make = !p.isTemplate
        for i in profiles.indices { profiles[i].isTemplate = false }
        if let i = profiles.firstIndex(where: { $0.id == p.id }) { profiles[i].isTemplate = make }
        saveProfiles()
    }

    func move(_ p: Profile, by delta: Int) {
        guard let i = profiles.firstIndex(of: p), profiles.indices.contains(i + delta) else { return }
        profiles.swapAt(i, i + delta)
        saveProfiles()
    }

    func remove(_ p: Profile) {
        profiles.removeAll { $0.id == p.id }
        saveProfiles()
    }

    func launch(_ p: Profile) {
        Claude.launch(p)
        if let pid = running[p.id] { NSRunningApplication(processIdentifier: pid)?.activate() }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { self.refresh() }
    }

    func openFolder(_ p: Profile) {
        if FileManager.default.fileExists(atPath: p.dataDir.path) {
            NSWorkspace.shared.open(p.dataDir)
        } else {
            alert = AlertItem(title: "Folder belum ada", message: "Folder dibuat otomatis saat instance ini pertama kali dibuka.\n\n" + p.dataDir.path)
        }
    }

    func copySetup(from: Profile, to: Profile, options: CopyOptions) {
        if isRunning(to) {
            alert = AlertItem(title: "Tutup dulu", message: "\"\(to.name)\" sedang berjalan. Tutup instance itu dulu, karena Claude menimpa setting-nya saat keluar.")
            return
        }
        do { alert = AlertItem(title: "Setup disalin", message: try SetupCopier.copy(from: from, to: to, options: options)) }
        catch { alert = AlertItem(title: "Gagal menyalin", message: error.localizedDescription) }
    }

    // MARK: arrange

    func openAndArrange() async {
        guard arrangeStatus == nil else { return }
        guard Arranger.trusted(prompt: true) else {
            alert = AlertItem(title: "Izin Accessibility diperlukan",
                              message: "Buka System Settings \u{2192} Privacy & Security \u{2192} Accessibility, aktifkan \"Claude Launcher\", lalu klik \"Buka & rapikan\" lagi.")
            return
        }
        let targets = profiles.filter { layout.includes($0) }
        guard !targets.isEmpty else {
            alert = AlertItem(title: "Belum ada instance", message: "Pilih instance yang mau dirapikan lewat tombol pengaturan tata letak.")
            return
        }
        arrangeStatus = "Membuka..."
        defer { arrangeStatus = nil; refresh() }

        var procs = await Task.detached { Claude.runningMains() }.value
        var launched = false
        for t in targets {
            if let pid = procs[Claude.key(t)], Arranger.mainWindow(pid: pid) != nil { continue }
            Claude.launch(t)
            launched = true
        }

        let deadline = Date().addingTimeInterval(30)
        var windows: [AXUIElement?] = []
        while true {
            procs = await Task.detached { Claude.runningMains() }.value
            windows = targets.map { t in procs[Claude.key(t)].flatMap { Arranger.mainWindow(pid: $0) } }
            let found = windows.compactMap { $0 }.count
            arrangeStatus = "Menunggu \(found)/\(targets.count)"
            if found == targets.count || Date() > deadline { break }
            try? await Task.sleep(nanoseconds: 700_000_000)
        }

        let ready = windows.compactMap { $0 }
        let monitors = Arranger.monitors()
        guard let monitor = monitors.first(where: { $0.id == layout.monitor }) ?? monitors.first else { return }
        let zones = Templates.resolve(layout.layout, ready.count, customs)
        arrangeStatus = "Merapikan..."
        // Claude restores its own saved window frame on startup; arrange after that settles.
        if launched { try? await Task.sleep(nanoseconds: 1_500_000_000) }
        Arranger.arrange(ready, on: monitor, zones: zones, gap: layout.gap)
        if launched {
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            Arranger.arrange(ready, on: monitor, zones: zones, gap: layout.gap)
        }
        if ready.count < targets.count {
            alert = AlertItem(title: "Sebagian belum muncul",
                              message: "\(ready.count) dari \(targets.count) jendela dirapikan. Sisanya belum muncul dalam 30 detik; klik \"Buka & rapikan\" lagi setelah terbuka.")
        }
    }
}

// MARK: - Buttons

enum ButtonKind { case primary, secondary, danger, icon }

struct LauncherButtonStyle: ButtonStyle {
    var kind: ButtonKind = .secondary
    func makeBody(configuration: Configuration) -> some View {
        StyledButton(configuration: configuration, kind: kind)
    }

    struct StyledButton: View {
        let configuration: ButtonStyleConfiguration
        let kind: ButtonKind
        @State private var hover = false
        @Environment(\.isEnabled) private var enabled

        var body: some View {
            let fill: Color = {
                switch kind {
                case .primary: return hover ? .accentHover : .accent
                case .danger: return hover ? .danger.opacity(0.85) : .danger
                default: return hover ? .surfaceHover : .surface
                }
            }()
            configuration.label
                .font(.system(size: 13, weight: .medium))
                .foregroundColor(kind == .primary ? .black : kind == .danger ? .white : .text)
                .padding(.horizontal, kind == .icon ? 0 : 18)
                .frame(minWidth: kind == .icon ? 36 : nil, minHeight: 36)
                .background(Capsule().fill(fill))
                .overlay(Capsule().stroke(kind == .secondary || kind == .icon ? (hover ? Color.borderHover : .border) : .clear))
                .opacity(configuration.isPressed ? 0.85 : (enabled ? 1 : 0.5))
                .contentShape(Rectangle())
                .onHover { hover = $0 }
        }
    }
}

extension View {
    func launcherButton(_ kind: ButtonKind = .secondary) -> some View { buttonStyle(LauncherButtonStyle(kind: kind)) }
}

// MARK: - Main window

enum SheetKind: Identifiable {
    case add, rename(Profile), copy(Profile), layout, savePreset
    var id: String {
        switch self {
        case .add: return "add"
        case .savePreset: return "save-preset"
        case .rename(let p): return "rename-" + p.id
        case .copy(let p): return "copy-" + p.id
        case .layout: return "layout"
        }
    }
}

struct ContentView: View {
    @EnvironmentObject var model: AppModel
    @State private var sheet: SheetKind?
    @State private var confirmRemove: Profile?

    var body: some View {
        VStack(spacing: 0) {
            header
            ScrollView {
                LazyVStack(spacing: 8) {
                    ForEach(model.profiles) { p in
                        ProfileCard(profile: p, sheet: $sheet, confirmRemove: $confirmRemove)
                    }
                }
                .padding(.horizontal, 24)
                .padding(.vertical, 4)
            }
            Text("Klik dua kali kartu untuk membuka  \u{00B7}  Klik kanan untuk opsi")
                .font(.system(size: 12))
                .foregroundColor(.muted)
                .padding(.vertical, 10)
        }
        .background(Color.bg)
        .frame(minWidth: 600, minHeight: 420)
        .sheet(item: $sheet) { kind in
            switch kind {
            case .add:
                NameSheet(title: "Tambah instance",
                          message: "Beri nama instance baru. Login dan datanya tersimpan terpisah dari instance lain." +
                            (model.profiles.first(where: { $0.isTemplate }).map { "\n\nSetup disalin otomatis dari template \"\($0.name)\"." } ?? ""),
                          initial: "", okText: "Tambah",
                          hint: { $0.isEmpty ? "" : "Folder data: ~/.claude-launcher/profiles/" + model.newId(for: $0) }) { model.add(name: $0) }
            case .rename(let p):
                NameSheet(title: "Ganti nama", message: "Nama tampilan untuk instance ini. Folder data dan login tidak berubah.",
                          initial: p.name, okText: "Simpan", hint: { _ in "" }) { name in model.update(p) { $0.name = name } }
            case .copy(let p):
                CopySheet(target: p)
            case .layout:
                LayoutEditorView()
            case .savePreset:
                NameSheet(title: "Simpan preset",
                          message: "Simpan tata letak sekarang sebagai preset: " +
                            "\(model.activeCount) instance aktif (" + model.profiles.filter { model.layout.includes($0) }.map { $0.name }.joined(separator: ", ") +
                            "), layout, monitor, dan jarak.\nNama yang sama akan menimpa preset lama.",
                          initial: "", okText: "Simpan", hint: { _ in "" }) { model.savePreset(name: $0) }
            }
        }
        .onAppear {
            // Started at login: stay in the menu bar instead of opening the window.
            if model.autostarted && model.settings.closeToTray {
                DispatchQueue.main.async {
                    NSApp.windows.filter { $0.isVisible && $0.canBecomeMain }.forEach { $0.close() }
                }
            }
        }
        .alert(item: $model.alert) { a in Alert(title: Text(a.title), message: Text(a.message)) }
        .alert("Hapus dari daftar?", isPresented: Binding(get: { confirmRemove != nil }, set: { if !$0 { confirmRemove = nil } })) {
            Button("Hapus", role: .destructive) { if let p = confirmRemove { model.remove(p) } }
            Button("Batal", role: .cancel) {}
        } message: {
            Text("\"\(confirmRemove?.name ?? "")\" dihapus dari launcher. Folder data dan login tidak dihapus.")
        }
    }

    var header: some View {
        HStack(alignment: .center) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Claude Launcher").font(.system(size: 26, weight: .semibold)).tracking(-0.6).foregroundColor(.text)
                Text("\(model.profiles.count) instance  \u{00B7}  \(model.running.count) berjalan  \u{00B7}  \(model.activeCount) aktif")
                    .font(.system(size: 12)).foregroundColor(.muted)
            }
            Spacer()
            Menu { appMenu } label: { Image(systemName: "line.3.horizontal").foregroundColor(.text) }
                .menuStyle(.borderlessButton)
                .menuIndicator(.hidden)
                .frame(width: 36, height: 36)
                .background(Circle().fill(Color.bg))
                .overlay(Circle().stroke(Color.border))
                .help("Preset, tutup semua, dan pengaturan")
            Button { sheet = .layout } label: { Image(systemName: "gearshape") }
                .launcherButton(.icon)
                .help("Atur tata letak jendela")
            Button(model.arrangeStatus ?? "Buka & rapikan (\(model.activeCount))") { Task { await model.openAndArrange() } }
                .launcherButton()
                .disabled(model.arrangeStatus != nil)
                .help("Buka instance yang dipilih lalu susun jendelanya")
            Button("+  Tambah") { sheet = .add }.launcherButton(.primary)
        }
        .padding(.horizontal, 24)
        .padding(.top, 20)
        .padding(.bottom, 16)
    }
}

// "5 jam [bar] 24%   Mingguan [bar] 38%   · data 30 Okt"
struct UsageRow: View {
    let usage: UsageSnapshot?
    private static let id = Locale(identifier: "id_ID")

    var body: some View {
        HStack(spacing: 12) {
            if let u = usage {
                if let w = u.fiveHour { meter("5 jam", w) }
                if let w = u.weekly { meter("Mingguan", w) }
                Text("\u{00B7} data " + u.taken.formatted(.dateTime.day().month(.abbreviated).locale(UsageRow.id)))
                    .foregroundColor(.muted).lineLimit(1)
            } else {
                Text("Limit: belum ada data").foregroundColor(.muted)
            }
        }
        .font(.system(size: 11))
        .help(tooltip)
    }

    func meter(_ label: String, _ w: UsageWindow) -> some View {
        let value = w.hasReset ? 0 : min(1, max(0, w.utilization ?? 0))
        return HStack(spacing: 6) {
            Text(label).foregroundColor(.muted)
            ZStack(alignment: .leading) {
                Capsule().fill(Color.border).frame(width: 44, height: 4)
                if value > 0 { Capsule().fill(value >= 0.9 ? Color.danger : .text).frame(width: max(4, 44 * value), height: 4) }
            }
            Text(w.hasReset ? "reset" : "\(Int((value * 100).rounded()))%").foregroundColor(w.hasReset ? .muted : .text)
        }
    }

    var tooltip: String {
        guard let u = usage else { return "Belum ada snapshot limit di cache instance ini." }
        let fmt = Date.FormatStyle().weekday(.abbreviated).day().month(.abbreviated).hour().minute().locale(UsageRow.id)
        var lines: [String] = []
        for (label, w) in [("5 jam", u.fiveHour), ("Mingguan", u.weekly)] {
            guard let w = w else { continue }
            let reset = w.resetsAt?.formatted(fmt) ?? "?"
            lines.append(w.hasReset ? "\(label): sudah reset (\(reset))" : "\(label): \(Int(((w.utilization ?? 0) * 100).rounded()))%, reset \(reset)")
        }
        lines.append("")
        lines.append("Snapshot terakhir di cache: " + u.taken.formatted(Date.FormatStyle().day().month(.abbreviated).year().hour().minute().locale(UsageRow.id)))
        lines.append("Bukan angka live; buka popup limit di Claude untuk angka terkini.")
        return lines.joined(separator: "\n")
    }
}

struct PillSwitch: View {
    @Binding var isOn: Bool

    var body: some View {
        Button { withAnimation(.easeOut(duration: 0.15)) { isOn.toggle() } } label: {
            ZStack(alignment: isOn ? .trailing : .leading) {
                Capsule().fill(isOn ? Color.accent : .bg)
                Capsule().stroke(isOn ? Color.accent : .borderHover)
                Circle().fill(isOn ? Color.black : .muted).frame(width: 16, height: 16).padding(3)
            }
            .frame(width: 40, height: 22)
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
    }
}

extension ContentView {
    @ViewBuilder var appMenu: some View {
        Section("Preset") {
            ForEach(model.presets) { p in
                Button(p.label) { Task { await model.runPreset(p) } }
            }
            if model.presets.isEmpty { Text("Belum ada preset") }
            Button("Simpan tata letak sekarang sebagai preset\u{2026}") { sheet = .savePreset }
            if !model.presets.isEmpty {
                Menu("Hapus preset") {
                    ForEach(model.presets) { p in Button(p.name) { model.deletePreset(p) } }
                }
            }
        }
        Divider()
        Button("Tutup semua instance", role: .destructive) { model.closeAll() }
        Divider()
        Toggle("Jalan otomatis saat login", isOn: Binding(get: { model.settings.autoStart }, set: { model.setAutoStart($0) }))
        Toggle("Buka & rapikan otomatis saat launcher jalan otomatis",
               isOn: Binding(get: { model.settings.arrangeOnStart }, set: { v in model.update { $0.arrangeOnStart = v } }))
        Toggle("Tutup jendela = tetap jalan di menu bar",
               isOn: Binding(get: { model.settings.closeToTray }, set: { v in model.update { $0.closeToTray = v } }))
        Toggle("Hotkey global (\u{2303}\u{2325}1\u{2013}9, \u{2303}\u{2325}0)", isOn: Binding(get: { model.settings.hotkeys }, set: { model.setHotkeys($0) }))
        Button("Daftar hotkey\u{2026}") { model.hotkeyHelp() }
        Divider()
        Button("Keluar dari launcher") { NSApp.terminate(nil) }
    }
}

// Menu bar ("tray") menu.
struct TrayMenu: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Button("Buka Claude Launcher") {
            openWindow(id: "main")
            NSApp.activate(ignoringOtherApps: true)
        }
        Divider()
        Text("Instance")
        ForEach(Array(model.profiles.enumerated()), id: \.element.id) { i, p in
            Button((model.isRunning(p) ? "\u{25CF}  " : "\u{25CB}  ") + p.name + (i < 9 ? "    \u{2303}\u{2325}\(i + 1)" : "")) { model.launch(p) }
        }
        Divider()
        Button("Buka & rapikan (\(model.activeCount))") { Task { await model.openAndArrange() } }
        if !model.presets.isEmpty {
            Menu("Preset") {
                ForEach(model.presets) { p in Button(p.label) { Task { await model.runPreset(p) } } }
            }
        }
        Button("Tutup semua instance") { model.closeAll() }
        Divider()
        Button("Keluar") { NSApp.terminate(nil) }
    }
}

struct ProfileCard: View {
    @EnvironmentObject var model: AppModel
    let profile: Profile
    @Binding var sheet: SheetKind?
    @Binding var confirmRemove: Profile?
    @State private var hover = false

    var body: some View {
        let running = model.isRunning(profile)
        HStack(spacing: 14) {
            ZStack(alignment: .bottomTrailing) {
                Circle().fill(Color(hex: profile.color)).frame(width: 40, height: 40)
                    .overlay(Text(String(profile.name.trimmingCharacters(in: .whitespaces).prefix(1)).uppercased())
                        .font(.system(size: 16, weight: .semibold)).foregroundColor(.white))
                if running {
                    Circle().fill(Color.running).frame(width: 12, height: 12)
                        .overlay(Circle().stroke(hover ? Color.surfaceHover : .surface, lineWidth: 2.5))
                        .offset(x: 2, y: 2)
                }
            }
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 8) {
                    Text(profile.name).font(.system(size: 14, weight: .semibold)).foregroundColor(.text).lineLimit(1)
                    if profile.isTemplate {
                        Text("Template").font(.system(size: 11)).foregroundColor(.accent)
                            .padding(.horizontal, 7).padding(.vertical, 1)
                            .overlay(Capsule().stroke(Color.accent))
                    }
                }
                HStack(spacing: 0) {
                    Text(running ? "Berjalan" : "Tidak berjalan").foregroundColor(running ? .running : .muted)
                    let email = model.emails[profile.id]
                    Text("  \u{00B7}  " + (email ?? (profile.isDefault ? "Belum login" : profile.subtitle)))
                        .foregroundColor(email == nil ? .muted : .text).lineLimit(1).truncationMode(.middle)
                }
                .font(.system(size: 12))
                UsageRow(usage: model.usages[profile.id]).padding(.top, 3)
            }
            Spacer(minLength: 8)
            PillSwitch(isOn: Binding(get: { model.layout.includes(profile) }, set: { model.setActive(profile, $0) }))
                .help("Ikut \"Buka & rapikan\"")
                .padding(.trailing, 8)
            Button(running ? "Tampilkan" : "Buka") { model.launch(profile) }
                .launcherButton(.primary)
                .frame(minWidth: 96)
            Menu { menuItems } label: { Image(systemName: "ellipsis").foregroundColor(.text) }
                .menuStyle(.borderlessButton)
                .menuIndicator(.hidden)
                .frame(width: 36, height: 36)
                .background(Circle().fill(Color.bg))
                .overlay(Circle().stroke(Color.border))
        }
        .padding(.horizontal, 18)
        .frame(height: 96)
        .background(RoundedRectangle(cornerRadius: 22, style: .continuous).fill(hover ? Color.surfaceHover : .surface))
        .overlay(RoundedRectangle(cornerRadius: 22, style: .continuous).stroke(hover ? Color.borderHover : .border))
        .contentShape(Rectangle())
        .onHover { hover = $0 }
        .onTapGesture(count: 2) { model.launch(profile) }
        .contextMenu { menuItems }
    }

    @ViewBuilder var menuItems: some View {
        let index = model.profiles.firstIndex(of: profile) ?? 0
        Button(model.isRunning(profile) ? "Tampilkan" : "Buka") { model.launch(profile) }
        Button("Tutup instance") { model.closeInstance(profile) }.disabled(!model.isRunning(profile))
        Divider()
        Button("Ganti nama\u{2026}") { sheet = .rename(profile) }
        Button("Salin setup dari\u{2026}") { sheet = .copy(profile) }.disabled(model.profiles.count < 2)
        Button(profile.isTemplate ? "Berhenti jadi template" : "Jadikan template") { model.toggleTemplate(profile) }
        Menu("Warna") {
            ForEach(palette, id: \.hex) { c in
                Button { model.update(profile) { $0.color = c.hex } } label: {
                    Label { Text(c.name + (c.hex == profile.color ? "  \u{2713}" : "")) } icon: { Image(nsImage: swatch(c.hex)) }
                }
            }
        }
        Button("Pindah ke atas") { model.move(profile, by: -1) }.disabled(index == 0)
        Button("Pindah ke bawah") { model.move(profile, by: 1) }.disabled(index >= model.profiles.count - 1)
        Button("Buka folder data") { model.openFolder(profile) }
        Divider()
        Button("Hapus dari daftar", role: .destructive) { confirmRemove = profile }
    }
}

// MARK: - Sheets

struct DarkField: View {
    @Binding var text: String
    var placeholder = ""
    @FocusState private var focused: Bool

    var body: some View {
        TextField(placeholder, text: $text)
            .textFieldStyle(.plain)
            .font(.system(size: 14))
            .foregroundColor(.text)
            .padding(.horizontal, 16)
            .frame(height: 40)
            .background(Capsule().fill(Color.surface))
            .overlay(Capsule().stroke(focused ? Color.borderHover : .border))
            .focused($focused)
            .onAppear { focused = true }
    }
}

struct NameSheet: View {
    @Environment(\.dismiss) private var dismiss
    let title: String
    let message: String
    let initial: String
    let okText: String
    let hint: (String) -> String
    let onSubmit: (String) -> Void
    @State private var name = ""
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(title).font(.system(size: 15, weight: .semibold)).foregroundColor(.text)
            Text(message).font(.system(size: 13)).foregroundColor(.text).fixedSize(horizontal: false, vertical: true)
            DarkField(text: $name).onSubmit(submit)
            Text(error ?? hint(name.trimmingCharacters(in: .whitespaces)))
                .font(.system(size: 12)).foregroundColor(error == nil ? .muted : .danger).frame(height: 16)
            HStack {
                Spacer()
                Button("Batal") { dismiss() }.launcherButton().keyboardShortcut(.cancelAction)
                Button(okText, action: submit).launcherButton(.primary).keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(width: 420)
        .background(Color.bg)
        .onAppear { name = initial }
        .onChange(of: name) { error = nil }
    }

    func submit() {
        let value = name.trimmingCharacters(in: .whitespaces)
        if let e = AppModel.validateName(value) { error = e; return }
        onSubmit(value)
        dismiss()
    }
}

struct CheckRow: View {
    let title: String
    @Binding var on: Bool

    var body: some View {
        Button { on.toggle() } label: {
            HStack(spacing: 10) {
                ZStack {
                    RoundedRectangle(cornerRadius: 5).fill(on ? Color.accent : .surface).frame(width: 18, height: 18)
                    RoundedRectangle(cornerRadius: 5).stroke(on ? Color.clear : .borderHover).frame(width: 18, height: 18)
                    if on { Image(systemName: "checkmark").font(.system(size: 11, weight: .bold)).foregroundColor(.black) }
                }
                Text(title).font(.system(size: 13)).foregroundColor(.text).lineLimit(1)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

struct CopySheet: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.dismiss) private var dismiss
    let target: Profile
    @State private var sourceId = ""
    @State private var o = CopyOptions()

    var body: some View {
        let sources = model.profiles.filter { $0.id != target.id }
        VStack(alignment: .leading, spacing: 10) {
            Text("Salin setup ke \"\(target.name)\"").font(.system(size: 15, weight: .semibold)).foregroundColor(.text)
            Text("Ambil dari").font(.system(size: 12)).foregroundColor(.muted).padding(.top, 6)
            Picker("", selection: $sourceId) {
                ForEach(sources) { p in Text(p.isTemplate ? p.name + "  (template)" : p.name).tag(p.id) }
            }
            .labelsHidden()
            Text("Yang disalin").font(.system(size: 12)).foregroundColor(.muted).padding(.top, 6)
            CheckRow(title: "MCP server (mode chat)", on: $o.mcp)
            CheckRow(title: "Preferensi aplikasi (Cowork, browser, izin, dll.)", on: $o.prefs)
            CheckRow(title: "Status on/off tool MCP", on: $o.toolToggles)
            CheckRow(title: "Preset sesi Claude Code", on: $o.presets)
            CheckRow(title: "Extension", on: $o.extensions)
            Text("Login tidak ikut disalin. File lama di tujuan dibackup ke folder launcher-backup.\nSkill, memory, CLAUDE.md dan MCP Claude Code (~/.claude) sudah sama di semua instance.")
                .font(.system(size: 12)).foregroundColor(.muted).fixedSize(horizontal: false, vertical: true).padding(.top, 6)
            HStack {
                Spacer()
                Button("Batal") { dismiss() }.launcherButton().keyboardShortcut(.cancelAction)
                Button("Salin") {
                    guard let source = sources.first(where: { $0.id == sourceId }) else { return }
                    dismiss()
                    model.copySetup(from: source, to: target, options: o)
                }
                .launcherButton(.primary)
                .keyboardShortcut(.defaultAction)
            }
            .padding(.top, 8)
        }
        .padding(24)
        .frame(width: 460)
        .background(Color.bg)
        .onAppear {
            sourceId = (sources.first(where: { $0.isTemplate }) ?? sources.first(where: { $0.isDefault }) ?? sources.first)?.id ?? ""
        }
    }
}

// MARK: - Layout editor

struct ZonePreview: View {
    let zones: [CGRect]
    var area = Color.surfaceHover

    var body: some View {
        GeometryReader { geo in
            ZStack(alignment: .topLeading) {
                Rectangle().fill(area)
                ForEach(Array(zones.enumerated()), id: \.offset) { i, z in
                    let r = CGRect(x: z.minX * geo.size.width + 1, y: z.minY * geo.size.height + 1,
                                   width: max(0, z.width * geo.size.width - 2), height: max(0, z.height * geo.size.height - 2))
                    Rectangle().fill(Color.bg)
                        .overlay(Rectangle().stroke(Color.border))
                        .overlay(Text("\(i + 1)").font(.system(size: 11)).foregroundColor(.muted).opacity(r.width > 16 && r.height > 16 ? 1 : 0))
                        .frame(width: r.width, height: r.height)
                        .offset(x: r.minX, y: r.minY)
                }
            }
        }
    }
}

struct LayoutCardView: View {
    let title: String
    let zones: [CGRect]
    let selected: Bool
    var onEdit: (() -> Void)?
    var onDelete: (() -> Void)?
    let onSelect: () -> Void
    @State private var hover = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text(title + "  \u{00B7}  \(zones.count) zona")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(selected ? .accent : .text)
                    .lineLimit(1)
                Spacer()
                if let onEdit = onEdit {
                    Button(action: onEdit) { Image(systemName: "pencil") }.buttonStyle(.plain).foregroundColor(.muted).help("Edit")
                }
                if let onDelete = onDelete {
                    Button(action: onDelete) { Image(systemName: "trash") }.buttonStyle(.plain).foregroundColor(.muted).help("Hapus")
                }
            }
            ZonePreview(zones: zones).frame(height: 100)
        }
        .padding(16)
        .frame(width: 210)
        .background(RoundedRectangle(cornerRadius: 20, style: .continuous).fill(hover ? Color.surfaceHover : .surface))
        .overlay(RoundedRectangle(cornerRadius: 20, style: .continuous).stroke(selected ? Color.accent : .border, lineWidth: selected ? 1.5 : 1))
        .contentShape(Rectangle())
        .onHover { hover = $0 }
        .onTapGesture(perform: onSelect)
    }
}

struct MonitorTileView: View {
    let monitor: MonitorInfo
    let selected: Bool
    let onSelect: () -> Void
    @State private var hover = false

    var body: some View {
        let color: Color = selected ? .accent : .text
        VStack(spacing: 4) {
            Text("\(monitor.number)").font(.system(size: 24, weight: .semibold)).foregroundColor(color)
            Text("\(Int(monitor.frame.width * monitor.scale)) \u{00D7} \(Int(monitor.frame.height * monitor.scale))")
                .font(.system(size: 12)).foregroundColor(selected ? .accent : .muted)
            Text("\(monitor.name)").font(.system(size: 11)).foregroundColor(selected ? .accent : .muted).lineLimit(1)
        }
        .frame(width: 170, height: 88)
        .background(RoundedRectangle(cornerRadius: 20, style: .continuous).fill(hover ? Color.surfaceHover : .surface))
        .overlay(RoundedRectangle(cornerRadius: 20, style: .continuous).stroke(selected ? Color.accent : .border, lineWidth: selected ? 1.5 : 1))
        .contentShape(Rectangle())
        .onHover { hover = $0 }
        .onTapGesture(perform: onSelect)
    }
}

struct ZoneEditTarget: Identifiable {
    let id = UUID()
    var layout: ZoneLayout
    let isNew: Bool
}

struct LayoutEditorView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.dismiss) private var dismiss
    @State private var monitorId = ""
    @State private var layoutKey = "grid"
    @State private var gap = true
    @State private var ids: Set<String> = []
    @State private var editing: ZoneEditTarget?
    @State private var deleting: ZoneLayout?
    private let monitors = Arranger.monitors()

    private var count: Int { max(1, ids.count) }
    private var workSize: CGSize { (monitors.first { $0.id == monitorId } ?? monitors.first)?.visible.size ?? CGSize(width: 16, height: 9) }
    private let grid = [GridItem(.adaptive(minimum: 210, maximum: 210), spacing: 12, alignment: .topLeading)]

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 16) {
                ForEach(monitors) { m in
                    MonitorTileView(monitor: m, selected: m.id == monitorId) { monitorId = m.id }
                }
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 18)
            .background(Color.topBar)

            ScrollView {
                VStack(alignment: .leading, spacing: 10) {
                    section("Instance")
                    Text("Instance yang dibuka & dirapikan. Nomor zona mengikuti urutan di daftar launcher.")
                        .font(.system(size: 12)).foregroundColor(.muted)
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 170), alignment: .leading)], alignment: .leading, spacing: 8) {
                        ForEach(model.profiles) { p in
                            CheckRow(title: p.name, on: Binding(
                                get: { ids.contains(p.id) },
                                set: { if $0 { ids.insert(p.id) } else { ids.remove(p.id) } }))
                        }
                    }
                    section("Template")
                    LazyVGrid(columns: grid, alignment: .leading, spacing: 12) {
                        ForEach(Array(Templates.keys.enumerated()), id: \.offset) { i, key in
                            LayoutCardView(title: Templates.names[i], zones: Templates.zones(key, count), selected: layoutKey == key) { layoutKey = key }
                        }
                    }
                    section("Custom")
                    if model.customs.isEmpty {
                        Text("Belum ada layout custom. Klik \"+ Buat layout baru\" untuk membuat zona sendiri.")
                            .font(.system(size: 12)).foregroundColor(.muted)
                    }
                    LazyVGrid(columns: grid, alignment: .leading, spacing: 12) {
                        ForEach(model.customs) { c in
                            LayoutCardView(title: c.name, zones: c.zones, selected: layoutKey == Templates.customPrefix + c.name,
                                           onEdit: { editing = ZoneEditTarget(layout: c, isNew: false) },
                                           onDelete: { deleting = c }) { layoutKey = Templates.customPrefix + c.name }
                        }
                    }
                }
                .padding(.horizontal, 24)
                .padding(.bottom, 16)
            }

            HStack(spacing: 8) {
                CheckRow(title: "Beri jarak antar jendela", on: $gap)
                Spacer()
                Button("+  Buat layout baru") { newLayout() }.launcherButton().padding(.trailing, 8)
                Button("Batal") { dismiss() }.launcherButton().keyboardShortcut(.cancelAction)
                Button("Simpan") { save() }.launcherButton(.primary).keyboardShortcut(.defaultAction)
            }
            .padding(.horizontal, 24)
            .padding(.vertical, 16)
        }
        .frame(width: 1000, height: 740)
        .background(Color.bg)
        .onAppear {
            let s = model.layout
            monitorId = monitors.contains { $0.id == s.monitor } ? s.monitor : (monitors.first?.id ?? "")
            layoutKey = s.layout
            gap = s.gap
            ids = Set(model.profiles.filter { s.includes($0) }.map { $0.id })
        }
        .sheet(item: $editing) { target in
            ZoneEditorView(target: target, area: workSize) { result in
                if target.isNew {
                    model.customs.append(result)
                } else if let i = model.customs.firstIndex(where: { $0.id == result.id }) {
                    let wasSelected = layoutKey == Templates.customPrefix + model.customs[i].name
                    model.customs[i] = result
                    if wasSelected { layoutKey = Templates.customPrefix + result.name }
                }
                if target.isNew { layoutKey = Templates.customPrefix + result.name }
                model.saveCustoms()
            }
        }
        .alert("Hapus layout?", isPresented: Binding(get: { deleting != nil }, set: { if !$0 { deleting = nil } })) {
            Button("Hapus", role: .destructive) {
                if let d = deleting {
                    if layoutKey == Templates.customPrefix + d.name { layoutKey = "grid" }
                    model.customs.removeAll { $0.id == d.id }
                    model.saveCustoms()
                }
            }
            Button("Batal", role: .cancel) {}
        } message: { Text("Layout \"\(deleting?.name ?? "")\" akan dihapus.") }
    }

    func section(_ title: String) -> some View {
        Text(title).font(.system(size: 17, weight: .semibold)).foregroundColor(.text).padding(.top, 16)
    }

    func newLayout() {
        var n = model.customs.count + 1
        while model.customs.contains(where: { $0.name == "Custom layout \(n)" }) { n += 1 }
        editing = ZoneEditTarget(layout: ZoneLayout(name: "Custom layout \(n)", zones: Templates.resolve(layoutKey, count, model.customs)), isNew: true)
    }

    func save() {
        model.layout.monitor = monitorId
        model.layout.layout = layoutKey
        model.layout.gap = gap
        model.layout.ids = model.profiles.filter { ids.contains($0.id) }.map { $0.id }
        model.saveLayout()
        dismiss()
    }
}

// MARK: - Zone editor

struct ZoneEditorView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.dismiss) private var dismiss
    let target: ZoneEditTarget
    let area: CGSize
    let onSave: (ZoneLayout) -> Void
    @State private var name = ""
    @State private var zones: [CGRect] = []
    @State private var selected: Int?
    @State private var error: String?

    var body: some View {
        VStack(spacing: 12) {
            HStack(spacing: 8) {
                DarkField(text: $name).frame(width: 300)
                Spacer()
                Button("+  Zona") { addZone() }.launcherButton()
                Button("Hapus zona") { removeSelected() }.launcherButton().disabled(selected == nil)
                Button("Kolom") { zones = Templates.zones("columns", max(2, zones.count)); selected = nil }.launcherButton()
                Button("Grid") { zones = Templates.zones("grid", max(2, zones.count)); selected = nil }.launcherButton()
            }
            ZoneCanvas(zones: $zones, selected: $selected, aspect: area)
            HStack {
                Text(error ?? "Geser zona untuk memindah  \u{00B7}  tarik tepi/sudut untuk ubah ukuran  \u{00B7}  klik dua kali untuk membagi (\u{21E7}: atas/bawah)  \u{00B7}  \u{232B} untuk hapus")
                    .font(.system(size: 12)).foregroundColor(error == nil ? .muted : .danger).lineLimit(2)
                Spacer()
                Button("Batal") { dismiss() }.launcherButton().keyboardShortcut(.cancelAction)
                Button("Simpan") { save() }.launcherButton(.primary).keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 1000, height: 700)
        .background(Color.bg)
        .onAppear { name = target.layout.name; zones = target.layout.zones }
    }

    func addZone() {
        let k = CGFloat(zones.count % 5)
        zones.append(CGRect(x: 0.3 + 0.03 * k, y: 0.25 + 0.03 * k, width: 0.4, height: 0.5))
        selected = zones.count - 1
    }

    func removeSelected() {
        guard let s = selected, zones.indices.contains(s) else { return }
        zones.remove(at: s)
        selected = nil
    }

    func save() {
        let value = name.trimmingCharacters(in: .whitespaces)
        if value.isEmpty { error = "Nama layout tidak boleh kosong."; return }
        if model.customs.contains(where: { $0.id != target.layout.id && $0.name.lowercased() == value.lowercased() }) {
            error = "Nama layout sudah dipakai."; return
        }
        if zones.isEmpty { error = "Minimal harus ada satu zona."; return }
        var result = target.layout
        result.name = value
        result.zones = zones
        onSave(result)
        dismiss()
    }
}

struct ZoneCanvas: View {
    @Binding var zones: [CGRect]
    @Binding var selected: Int?
    let aspect: CGSize
    @State private var dragStart: CGRect?
    @FocusState private var focused: Bool

    struct Edges: OptionSet {
        let rawValue: Int
        static let left = Edges(rawValue: 1), right = Edges(rawValue: 2), top = Edges(rawValue: 4), bottom = Edges(rawValue: 8)
        static let move: Edges = []
    }

    static let snapDistance: CGFloat = 0.015
    static let minSize: CGFloat = 0.05

    var body: some View {
        GeometryReader { geo in
            let area = fit(geo.size)
            ZStack(alignment: .topLeading) {
                Color.bg.contentShape(Rectangle()).onTapGesture { selected = nil; focused = true }
                Canvas { ctx, _ in
                    ctx.fill(Path(area), with: .color(.canvas))
                    var lines = Path()
                    for i in 1..<10 {
                        let x = area.minX + area.width * CGFloat(i) / 10, y = area.minY + area.height * CGFloat(i) / 10
                        lines.move(to: CGPoint(x: x, y: area.minY)); lines.addLine(to: CGPoint(x: x, y: area.maxY))
                        lines.move(to: CGPoint(x: area.minX, y: y)); lines.addLine(to: CGPoint(x: area.maxX, y: y))
                    }
                    ctx.stroke(lines, with: .color(.gridLine))
                    ctx.stroke(Path(area), with: .color(.border))
                }
                .allowsHitTesting(false)
                ForEach(Array(zones.indices), id: \.self) { i in
                    zoneView(i, area)
                }
            }
        }
        .focusable()
        .focused($focused)
        .focusEffectDisabled()
        .onDeleteCommand {
            guard let s = selected, zones.indices.contains(s) else { return }
            zones.remove(at: s)
            selected = nil
        }
    }

    func fit(_ size: CGSize) -> CGRect {
        let s = min(size.width / max(1, aspect.width), size.height / max(1, aspect.height))
        let w = aspect.width * s, h = aspect.height * s
        return CGRect(x: (size.width - w) / 2, y: (size.height - h) / 2, width: w, height: h)
    }

    @ViewBuilder func zoneView(_ i: Int, _ area: CGRect) -> some View {
        if zones.indices.contains(i) {
            let z = zones[i]
            let r = CGRect(x: area.minX + z.minX * area.width, y: area.minY + z.minY * area.height,
                           width: z.width * area.width, height: z.height * area.height).insetBy(dx: 1.5, dy: 1.5)
            let isSelected = selected == i
            ZStack {
                Rectangle().fill(Color.surface)
                if isSelected { Rectangle().fill(Color.accent.opacity(0.06)) }
                Rectangle().stroke(isSelected ? Color.accent : .borderHover, lineWidth: isSelected ? 2 : 1)
                if r.width > 70 && r.height > 70 {
                    VStack(spacing: 4) {
                        Text("\(i + 1)").font(.system(size: 30, weight: .semibold)).foregroundColor(isSelected ? .accent : .text)
                        Text("\(Int((z.width * aspect.width).rounded())) \u{00D7} \(Int((z.height * aspect.height).rounded()))   (\(Int((z.width * 100).rounded()))% \u{00D7} \(Int((z.height * 100).rounded()))%)")
                            .font(.system(size: 11)).foregroundColor(.muted)
                    }
                } else {
                    Text("\(i + 1)").font(.system(size: 13, weight: .semibold)).foregroundColor(.text)
                }
            }
            .frame(width: max(1, r.width), height: max(1, r.height))
            .contentShape(Rectangle())
            .onTapGesture(count: 2) { split(i) }
            .onTapGesture { selected = i; focused = true }
            .gesture(drag(i, .move, area))
            .overlay(handles(i, area))
            .overlay(alignment: .topTrailing) {
                if r.width > 60 && r.height > 50 {
                    Button {
                        zones.remove(at: i)
                        selected = nil
                    } label: {
                        Image(systemName: "xmark").font(.system(size: 11, weight: .semibold)).foregroundColor(.muted)
                            .frame(width: 24, height: 24)
                            .background(Circle().fill(Color.surfaceHover))
                    }
                    .buttonStyle(.plain)
                    .padding(8)
                }
            }
            .position(x: r.midX, y: r.midY)
        }
    }

    func handles(_ i: Int, _ area: CGRect) -> some View {
        let t: CGFloat = 8
        return ZStack {
            VStack { handle(i, .top, area, .resizeUpDown).frame(height: t); Spacer(); handle(i, .bottom, area, .resizeUpDown).frame(height: t) }
            HStack { handle(i, .left, area, .resizeLeftRight).frame(width: t); Spacer(); handle(i, .right, area, .resizeLeftRight).frame(width: t) }
            VStack {
                HStack { handle(i, [.top, .left], area, .crosshair).frame(width: 14, height: 14); Spacer(); handle(i, [.top, .right], area, .crosshair).frame(width: 14, height: 14) }
                Spacer()
                HStack { handle(i, [.bottom, .left], area, .crosshair).frame(width: 14, height: 14); Spacer(); handle(i, [.bottom, .right], area, .crosshair).frame(width: 14, height: 14) }
            }
        }
    }

    func handle(_ i: Int, _ edges: Edges, _ area: CGRect, _ cursor: NSCursor) -> some View {
        Color.clear
            .contentShape(Rectangle())
            .onHover { inside in if inside { cursor.push() } else { NSCursor.pop() } }
            .gesture(drag(i, edges, area))
    }

    func drag(_ i: Int, _ edges: Edges, _ area: CGRect) -> some Gesture {
        DragGesture(minimumDistance: 1)
            .onChanged { v in
                guard zones.indices.contains(i) else { return }
                if dragStart == nil { dragStart = zones[i]; selected = i; focused = true }
                guard let start = dragStart else { return }
                let dx = v.translation.width / area.width, dy = v.translation.height / area.height
                zones[i] = resized(start, i, edges, dx, dy)
            }
            .onEnded { _ in dragStart = nil }
    }

    func resized(_ r: CGRect, _ i: Int, _ edges: Edges, _ dx: CGFloat, _ dy: CGFloat) -> CGRect {
        var l = r.minX, t = r.minY, rr = r.maxX, b = r.maxY
        let minSize = ZoneCanvas.minSize
        if edges.isEmpty {
            let w = r.width, h = r.height
            l = clamp(l + dx, 0, 1 - w)
            t = clamp(t + dy, 0, 1 - h)
            let byLeft = snap(l, i, true), byRight = snap(l + w, i, true) - w
            l = clamp(abs(byLeft - l) <= abs(byRight - l) ? byLeft : byRight, 0, 1 - w)
            let byTop = snap(t, i, false), byBottom = snap(t + h, i, false) - h
            t = clamp(abs(byTop - t) <= abs(byBottom - t) ? byTop : byBottom, 0, 1 - h)
            rr = l + w
            b = t + h
        } else {
            if edges.contains(.left) { l = min(snap(clamp(l + dx, 0, 1), i, true), rr - minSize) }
            if edges.contains(.right) { rr = max(snap(clamp(rr + dx, 0, 1), i, true), l + minSize) }
            if edges.contains(.top) { t = min(snap(clamp(t + dy, 0, 1), i, false), b - minSize) }
            if edges.contains(.bottom) { b = max(snap(clamp(b + dy, 0, 1), i, false), t + minSize) }
        }
        return CGRect(x: l, y: t, width: rr - l, height: b - t)
    }

    func snap(_ v: CGFloat, _ selfIndex: Int, _ horizontal: Bool) -> CGFloat {
        var candidates: [CGFloat] = [0, 1]
        for (j, z) in zones.enumerated() where j != selfIndex {
            candidates += horizontal ? [z.minX, z.maxX] : [z.minY, z.maxY]
        }
        if let c = candidates.first(where: { abs(v - $0) < ZoneCanvas.snapDistance }) { return c }
        return (v * 100).rounded() / 100
    }

    func clamp(_ v: CGFloat, _ lo: CGFloat, _ hi: CGFloat) -> CGFloat { max(lo, min(hi, v)) }

    // Left/right, or top/bottom with Shift.
    func split(_ i: Int) {
        guard zones.indices.contains(i) else { return }
        let z = zones[i]
        let a: CGRect, b: CGRect
        if NSEvent.modifierFlags.contains(.shift) {
            guard z.height / 2 >= ZoneCanvas.minSize else { return }
            a = CGRect(x: z.minX, y: z.minY, width: z.width, height: z.height / 2)
            b = CGRect(x: z.minX, y: z.midY, width: z.width, height: z.height / 2)
        } else {
            guard z.width / 2 >= ZoneCanvas.minSize else { return }
            a = CGRect(x: z.minX, y: z.minY, width: z.width / 2, height: z.height)
            b = CGRect(x: z.midX, y: z.minY, width: z.width / 2, height: z.height)
        }
        zones[i] = a
        zones.insert(b, at: i + 1)
        selected = i + 1
    }
}

// MARK: - App

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { !AppModel.keepRunning }
}

@main
struct ClaudeLauncherApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
    @StateObject private var model = AppModel()

    var body: some Scene {
        Window("Claude Launcher", id: "main") {
            ContentView()
                .environmentObject(model)
                .preferredColorScheme(.dark)
        }
        .defaultSize(width: 680, height: 560)
        .windowResizability(.contentMinSize)

        MenuBarExtra("Claude Launcher", systemImage: "square.grid.2x2") {
            TrayMenu().environmentObject(model)
        }
    }
}
