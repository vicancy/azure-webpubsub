import { TokenCredential, GetTokenOptions, AccessToken, CredentialUnavailableError } from "@azure/identity";
import { execFile } from "child_process";
import * as fs from "fs";
import * as path from "path";
import * as os from "os";
import * as util from "util";

const execFilePromise = util.promisify(execFile);

export interface VisualStudioCredentialOptions {
  tenantId: string;
  processTimeout?: number;
  isChainedCredential?: boolean;
  additionallyAllowedTenants?: string[];
}

export class VisualStudioCredential implements TokenCredential {
  private static readonly TokenProviderFilePath = path.join(".IdentityService", "AzureServiceAuth", "tokenprovider.json");
  private readonly tenantId?: string;
  private readonly processTimeout: number;
  private readonly isChainedCredential: boolean;
  private readonly additionallyAllowedTenants?: string[];

  constructor(options: VisualStudioCredentialOptions) {
    this.tenantId = options.tenantId;
    this.processTimeout = options.processTimeout || 30000; // Default 30 seconds
    this.isChainedCredential = options.isChainedCredential || false;
    this.additionallyAllowedTenants = options.additionallyAllowedTenants;
  }

  public async getToken(scopes: string | string[], options?: GetTokenOptions): Promise<AccessToken | null> {
    const tokenProviderPath = this.getTokenProviderPath();
    const tokenProviders = this.getTokenProviders(tokenProviderPath);
    const resource = this.scopesToResource(scopes);

    for (const tokenProvider of tokenProviders) {
      try {
        const result = await this.runProcess(tokenProvider, resource);
        const { access_token, expires_on } = JSON.parse(result);
        return {
          token: access_token,
          expiresOnTimestamp: new Date(expires_on).getTime(),
        };
      } catch (error) {
        throw new CredentialUnavailableError(`VisualStudioCredential failed to get token: ${error}`);
      }
    }

    throw new CredentialUnavailableError("No installed instance of Visual Studio was found or able to provide an access token.");
  }

  private getTokenProviderPath(): string {
    let baseFolder: string;
    if (os.platform() === "win32") {
      baseFolder = process.env.LOCALAPPDATA || os.homedir();
    } else {
      baseFolder = os.homedir();
    }

    return path.join(baseFolder, VisualStudioCredential.TokenProviderFilePath);
  }

  private getTokenProviders(tokenProviderPath: string): VisualStudioTokenProvider[] {
    const content = this.getTokenProviderContent(tokenProviderPath);
    const json = JSON.parse(content);
    const providers = json.TokenProviders.map((provider: any) => {
      return new VisualStudioTokenProvider(provider.Path, provider.Arguments || [], provider.Preference);
    });

    return providers.sort((a: VisualStudioTokenProvider, b: VisualStudioTokenProvider) => a.compareTo(b));
  }

  private getTokenProviderContent(tokenProviderPath: string): string {
    try {
      const content = fs.readFileSync(tokenProviderPath, "utf-8");
      if (content.charCodeAt(0) === 0xfeff) {
        return content.slice(1);
      }
      return content;
    } catch (error) {
      throw new CredentialUnavailableError(`Visual Studio Token provider file not found or can't be accessed at ${tokenProviderPath}: ${error}`);
    }
  }

  private async runProcess(tokenProvider: VisualStudioTokenProvider, resource: string): Promise<string> {
    const args = [...tokenProvider.arguments, "--resource", resource];

    if (this.tenantId) {
      args.push("--tenant", this.tenantId);
    }

    try {
      const { stdout } = await execFilePromise(tokenProvider.path, args, { timeout: this.processTimeout });
      return stdout;
    } catch (error) {
      throw new CredentialUnavailableError(`Process "${tokenProvider.path}" failed to get access token: ${error}`);
    }
  }

  private scopesToResource(scopes: string | string[]): string {
    if (typeof scopes === "string") {
      scopes = [scopes];
    }

    // Simplified logic for converting scopes to a resource.
    return scopes[0];
  }
}

class VisualStudioTokenProvider {
  public path: string;
  public arguments: string[];
  private preference: number;

  constructor(path: string, args: string[], preference: number) {
    this.path = path;
    this.arguments = args.map((arg) => {
      if (arg[0] === '"') {
        // normalize the argument to remove the quotes, as they are not needed when passing the argument to execFile
        return arg.slice(1, -1);
      }
      return arg;
    });
    this.preference = preference;
  }

  public compareTo(other: VisualStudioTokenProvider): number {
    return this.preference - other.preference;
  }
}
