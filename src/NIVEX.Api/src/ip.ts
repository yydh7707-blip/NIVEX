export const DEVICES_PER_IP = 3;

function parseV4(text: string): number[] | null {
  const parts = text.split(".");
  if (parts.length !== 4) return null;
  const bytes: number[] = [];
  for (const part of parts) {
    if (!/^(0|[1-9][0-9]{0,2})$/.test(part)) return null;
    const value = Number(part);
    if (value > 255) return null;
    bytes.push(value);
  }
  return bytes;
}

function parseV6(text: string): number[] | null {
  if (!/^[0-9a-f:.]+$/.test(text) || text.includes(":::")) return null;
  const doubleColon = text.indexOf("::");
  if (doubleColon !== -1 && text.indexOf("::", doubleColon + 1) !== -1) return null;
  const headText = doubleColon === -1 ? text : text.slice(0, doubleColon);
  const tailText = doubleColon === -1 ? "" : text.slice(doubleColon + 2);
  const head = groupsToWords(headText === "" ? [] : headText.split(":"), doubleColon === -1);
  const tail = groupsToWords(tailText === "" ? [] : tailText.split(":"), true);
  if (!head || !tail) return null;
  if (doubleColon === -1) return head.length === 8 ? head : null;
  const missing = 8 - head.length - tail.length;
  if (missing < 1) return null;
  return [...head, ...Array<number>(missing).fill(0), ...tail];
}

function groupsToWords(groups: string[], allowV4Tail: boolean): number[] | null {
  const words: number[] = [];
  for (let i = 0; i < groups.length; i += 1) {
    const group = groups[i]!;
    if (group.includes(".")) {
      if (!allowV4Tail || i !== groups.length - 1) return null;
      const v4 = parseV4(group);
      if (!v4) return null;
      words.push((v4[0]! << 8) | v4[1]!, (v4[2]! << 8) | v4[3]!);
    } else {
      if (!/^[0-9a-f]{1,4}$/.test(group)) return null;
      words.push(parseInt(group, 16));
    }
  }
  return words;
}

export interface ParsedIp {
  /** Canonical text used for comparing what the user typed with what the server sees. */
  canonical: string;
  /** Quota key: the address for IPv4, the /64 network for IPv6. */
  quotaKey: string;
}

export function parseIp(input: string): ParsedIp | null {
  let text = input.trim().toLowerCase();
  if (text.startsWith("[") && text.endsWith("]")) text = text.slice(1, -1);
  const zone = text.indexOf("%");
  if (zone !== -1) text = text.slice(0, zone);
  if (text.length === 0 || text.length > 45) return null;

  const v4 = text.includes(":") ? null : parseV4(text);
  if (v4) {
    const canonical = v4.join(".");
    return { canonical, quotaKey: canonical };
  }
  const words = text.includes(":") ? parseV6(text) : null;
  if (!words) return null;
  const mapped = words.slice(0, 5).every((word) => word === 0) && words[5] === 0xffff;
  if (mapped) {
    const canonical = [words[6]! >> 8, words[6]! & 255, words[7]! >> 8, words[7]! & 255].join(".");
    return { canonical, quotaKey: canonical };
  }
  return {
    canonical: words.map((word) => word.toString(16)).join(":"),
    quotaKey: `${words.slice(0, 4).map((word) => word.toString(16)).join(":")}::/64`,
  };
}
