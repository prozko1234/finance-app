// Packs the built web app into the zip the native shell downloads as a live update.
//
// The version is derived from the content, not from a counter someone has to remember to
// bump: a deploy that changed nothing produces the same version, and the phone does not
// download a bundle identical to the one it is running.
//
//   node scripts/make-bundle.mjs          # after npm run build
//
// Output: bundle/<version>.zip and bundle/bundle.json, both served from wwwroot in the image.
//
// The archive is written by hand instead of shelling out to `zip`: the image is node:alpine
// and its build runs without network, so an `apk add zip` there is a deploy that fails on a
// DNS hiccup. Everything below is Node's own zlib and crypto.

import { createHash } from 'node:crypto'
import { deflateRawSync } from 'node:zlib'
import { readdirSync, readFileSync, rmSync, mkdirSync, writeFileSync, statSync } from 'node:fs'
import { join, relative, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..')
const DIST = join(ROOT, 'dist')
const OUT = join(ROOT, 'bundle')

if (!existsDir(DIST)) {
  console.error('!! немає dist — спершу npm run build')
  process.exit(1)
}

function existsDir(path) {
  try {
    return statSync(path).isDirectory()
  } catch {
    return false
  }
}

/** Every file under dir, as paths relative to it — the archive's entry names. */
function walk(dir, base = dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = join(dir, entry.name)
    return entry.isDirectory() ? walk(full, base) : [relative(base, full)]
  })
}

const CRC_TABLE = Array.from({ length: 256 }, (_, i) => {
  let c = i
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
  return c >>> 0
})

function crc32(buf) {
  let c = 0xffffffff
  for (const byte of buf) c = CRC_TABLE[(c ^ byte) & 0xff] ^ (c >>> 8)
  return (c ^ 0xffffffff) >>> 0
}

/**
 * A ZIP with one deflated entry per file, no directory entries. Names are stored relative to
 * dist, so index.html sits at the root of the archive — that is where the updater expects to
 * find it after unpacking.
 */
function zip(dir) {
  const locals = []
  const central = []
  let offset = 0

  for (const name of walk(dir).sort()) {
    const raw = readFileSync(join(dir, name))
    const compressed = deflateRawSync(raw)
    const nameBytes = Buffer.from(name.split('\\').join('/'), 'utf8')
    const crc = crc32(raw)

    const local = Buffer.alloc(30 + nameBytes.length)
    local.writeUInt32LE(0x04034b50, 0)
    local.writeUInt16LE(20, 4) // version needed: 2.0, deflate
    local.writeUInt16LE(1 << 11, 6) // UTF-8 names
    local.writeUInt16LE(8, 8) // deflate
    // Timestamps are fixed rather than taken from the filesystem: the bundle's identity is its
    // content, and a clock in the archive would make two identical builds differ.
    local.writeUInt16LE(0, 10) // time
    local.writeUInt16LE(0x21, 12) // date: 1980-01-01
    local.writeUInt32LE(crc, 14)
    local.writeUInt32LE(compressed.length, 18)
    local.writeUInt32LE(raw.length, 22)
    local.writeUInt16LE(nameBytes.length, 26)
    local.writeUInt16LE(0, 28) // extra field length
    nameBytes.copy(local, 30)
    locals.push(local, compressed)

    const entry = Buffer.alloc(46 + nameBytes.length)
    entry.writeUInt32LE(0x02014b50, 0)
    entry.writeUInt16LE(20, 4) // version made by
    local.copy(entry, 6, 4, 30) // flags…name length, identical to the local header
    entry.writeUInt16LE(0, 32) // comment length
    entry.writeUInt16LE(0, 34) // disk number
    entry.writeUInt16LE(0, 36) // internal attributes
    entry.writeUInt32LE((0o100644 << 16) >>> 0, 38) // external attributes: regular file, rw-r--r--
    entry.writeUInt32LE(offset, 42)
    nameBytes.copy(entry, 46)
    central.push(entry)

    offset += local.length + compressed.length
  }

  const directory = Buffer.concat(central)
  const end = Buffer.alloc(22)
  end.writeUInt32LE(0x06054b50, 0)
  end.writeUInt16LE(central.length, 8)
  end.writeUInt16LE(central.length, 10)
  end.writeUInt32LE(directory.length, 12)
  end.writeUInt32LE(offset, 16)

  return Buffer.concat([...locals, directory, end])
}

const archive = zip(DIST)
const checksum = createHash('sha256').update(archive).digest('hex')

// Semver, because that is what the updater compares — and monotonic, so a later build always
// wins. The patch number is minutes since 2026-01-01: unique per build without a counter, and
// it stays inside the integer range for the next few thousand years.
const version = `1.0.${Math.floor((Date.now() / 1000 - 1767225600) / 60)}`

rmSync(OUT, { recursive: true, force: true })
mkdirSync(OUT, { recursive: true })
writeFileSync(join(OUT, `${version}.zip`), archive)
writeFileSync(
  join(OUT, 'bundle.json'),
  `${JSON.stringify({ version, checksum, file: `${version}.zip` }, null, 2)}\n`,
)

console.log(`bundle ${version} (${(archive.length / 1024).toFixed(0)}K)`)
