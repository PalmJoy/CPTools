// Releases share a repository but keep independent version streams.
export function selectRelease(releases, prefix) {
  if (!Array.isArray(releases)) return null;
  let best = null, bestVersion = null;
  for (const release of releases) {
    if (!release || release.draft !== false || release.prerelease !== false || typeof release.tag_name !== 'string' || !release.tag_name.startsWith(prefix)) continue;
    const text = release.tag_name.slice(prefix.length);
    if (!/^\d+\.\d+\.\d+$/.test(text)) continue;
    const version = text.split('.').map(Number);
    if (!version.every(n => Number.isSafeInteger(n) && n <= 2147483647)) continue;
    const newer = !bestVersion || version[0] > bestVersion[0] || version[0] === bestVersion[0] && (version[1] > bestVersion[1] || version[1] === bestVersion[1] && version[2] > bestVersion[2]);
    if (newer) {best = release; bestVersion = version;}
  }
  return best;
}
