import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const webAppRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const srcRoot = path.join(webAppRoot, 'src')
const messagesPath = path.join(srcRoot, 'i18n', 'messages.ts')
const messagePrefixes = new Set([
  'action',
  'app',
  'book',
  'detail',
  'discovery',
  'empty',
  'filter',
  'market',
  'metric',
  'nav',
  'readout',
  'risk',
  'state',
  'status',
  'strategy',
  'table',
  'view'
])
const mojibakePatterns = [
  /�/,
  /Ã/,
  /鑷/,
  /涓枃/,
  /甯傚満/,
  /鏁版嵁/,
  /绛涢/,
  /宸叉/
]
const bannedCopyPatterns = [
  /choose opportunities/i,
  /guaranteed/i,
  /investment advice/i,
  /tradable watchlist/i
]

const sourceFiles = await listSourceFiles(srcRoot)
const messagesSource = await readFile(messagesPath, 'utf8')
const zhCNKeys = extractMessageKeys(messagesSource, 'const zhCNMessages', '} as const')
const enUSKeys = extractMessageKeys(messagesSource, 'const enUSMessages', '} satisfies')
const requiredKeys = await collectRequiredMessageKeys(sourceFiles)
const failures = []

for (const [locale, keys] of [['zh-CN', zhCNKeys], ['en-US', enUSKeys]]) {
  for (const requiredKey of requiredKeys) {
    if (!keys.has(requiredKey)) {
      failures.push(`${locale} is missing message id "${requiredKey}".`)
    }
  }
}

for (const key of zhCNKeys) {
  if (!enUSKeys.has(key)) {
    failures.push(`en-US is missing zh-CN message id "${key}".`)
  }
}

for (const key of enUSKeys) {
  if (!zhCNKeys.has(key)) {
    failures.push(`zh-CN is missing en-US message id "${key}".`)
  }
}

for (const sourceFile of sourceFiles) {
  const source = await readFile(sourceFile, 'utf8')
  if (source.includes('defaultMessage')) {
    failures.push(`${relativePath(sourceFile)} still uses react-intl defaultMessage fallback.`)
  }

  for (const pattern of mojibakePatterns) {
    if (pattern.test(source)) {
      failures.push(`${relativePath(sourceFile)} contains likely mojibake matching ${pattern}.`)
    }
  }
}

for (const pattern of [...mojibakePatterns, ...bannedCopyPatterns]) {
  if (pattern.test(messagesSource)) {
    failures.push(`messages.ts contains blocked copy or encoding artifact matching ${pattern}.`)
  }
}

if (failures.length > 0) {
  console.error('i18n message check failed:')
  for (const failure of failures) {
    console.error(`- ${failure}`)
  }
  process.exit(1)
}

console.log(`i18n message check passed (${requiredKeys.size} rendered message ids, ${zhCNKeys.size} locale keys).`)

async function listSourceFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true })
  const files = await Promise.all(entries.map(async (entry) => {
    const fullPath = path.join(directory, entry.name)
    if (entry.isDirectory()) {
      return listSourceFiles(fullPath)
    }

    return /\.(ts|tsx)$/.test(entry.name) ? [fullPath] : []
  }))

  return files.flat()
}

function extractMessageKeys(source, startMarker, endMarker) {
  const start = source.indexOf(startMarker)
  if (start < 0) {
    throw new Error(`Could not find ${startMarker} in messages.ts.`)
  }

  const end = source.indexOf(endMarker, start)
  if (end < 0) {
    throw new Error(`Could not find ${endMarker} after ${startMarker} in messages.ts.`)
  }

  const block = source.slice(start, end)
  const keys = new Set()
  const keyPattern = /'([^']+)':/g
  for (const match of block.matchAll(keyPattern)) {
    keys.add(match[1])
  }

  return keys
}

async function collectRequiredMessageKeys(files) {
  const required = new Set()

  for (const file of files) {
    if (file === messagesPath) {
      continue
    }

    const source = await readFile(file, 'utf8')
    const messageIdPattern = /['"`]([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)['"`]/g
    for (const match of source.matchAll(messageIdPattern)) {
      const id = match[1]
      if (messagePrefixes.has(id.split('.')[0])) {
        required.add(id)
      }
    }
  }

  const controlRoomTypesPath = path.join(srcRoot, 'types', 'controlRoom.ts')
  const typeSource = await readFile(controlRoomTypesPath, 'utf8')
  const strategyStateMatch = /export type StrategyState = ([^\n]+)/.exec(typeSource)
  if (strategyStateMatch) {
    for (const stateMatch of strategyStateMatch[1].matchAll(/'([^']+)'/g)) {
      required.add(`state.${stateMatch[1]}`)
    }
  }

  return required
}

function relativePath(filePath) {
  return path.relative(webAppRoot, filePath).replaceAll(path.sep, '/')
}
