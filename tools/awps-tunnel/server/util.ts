export function tryParseInt(port: string | undefined): number | undefined {
  if (!port) return undefined;
  const number = parseInt(port);
  return isNaN(number) ? undefined : number;
}

// Function to extract headers and body from HTTP request raw data
export function extractHeadersAndBody(requestRaw) {
  const decoder = new StringDecoder('utf-8');
  let headers = {};
  let body = '';

  // Split raw data into headers and body
  const [rawHeaders, rawBody] = requestRaw.split('\r\n\r\n');
  
  // Parse headers
  rawHeaders.split('\r\n').forEach((line, index) => {
    if (index === 0) {
      // Skip the HTTP status line
      return;
    }
    
    const [key, value] = line.split(': ');
    headers[key.toLowerCase()] = value;
  });

  // Parse body
  if (rawBody) {
    body = decoder.write(rawBody);
  }

  return { headers, body };
}

export function dumpRawRequest(proxiedUrl: URL, message: { Url: string; HttpMethod: string; Headers?: Record<string, string[]>; Content?: Uint8Array }): string {
  const headers = message.Headers
    ? Object.entries(message.Headers)
        .map(([name, values]) => values.map((value) => `${name}: ${value}`).join("\r\n"))
        .join("\r\n")
    : "";

  const content = message.Content ? new TextDecoder().decode(message.Content) : "";

  return `${message.HttpMethod} ${proxiedUrl} HTTP/1.1\r\n${headers}\r\n\r\n${content}`;
}

export function getRawResponse(message: { StatusCode: number; Headers: Record<string, string[]>; Content: Uint8Array }) {
  const headers = message.Headers
    ? Object.entries(message.Headers)
        .map(([name, values]) => values.map((value) => `${name}: ${value}`).join("\r\n"))
        .join("\r\n")
    : "";

  const content = message.Content ? new TextDecoder().decode(message.Content) : "";
  return `HTTP/1.1 ${message.StatusCode}\r\n${headers}\r\n\r\n${content}`;
}

/**
 * Parse the url string to URL object, return undefined if the url is invalid.
 */
export function parseUrl(url: string, optionalScheme: false | "http" | "https" = false): URL | undefined {
  // only http and https schemes are supported
  const regex = /^(http|https):\/\/[^ "]+$/i;
  try {
    if (regex.test(url)) {
      return new URL(url);
    } else {
      if (optionalScheme) {
        // check if it is the ommitted scheme case
        if (!url.includes("://")) {
          return parseUrl(`${optionalScheme}://${url}`);
        }
      }

      return undefined;
    }
  } catch {
    return undefined;
  }
}
