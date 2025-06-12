import { HttpClient } from "@angular/common/http";
import { Injectable } from "@angular/core";
import { ReplaySubject } from "rxjs";
import { NomConfig } from "../models/nom-config";

@Injectable({
  providedIn: "root",
})
export class NomConfigService {
  private _config: NomConfig;

  get config(): NomConfig {
    return this._config;
  }

  readonly DEFAULT_DEV_URI = "/assets/nom-config.development.json";
  readonly DEFAULT_URI = "/assets/nom-config.json";

  settingsLoaded: ReplaySubject<void> = new ReplaySubject<void>(1);

  constructor(private httpClient: HttpClient) {
    this._config = new NomConfig();
    this.getSettings();
  }

  getSettings(uri: string = this.DEFAULT_DEV_URI) {
    this.httpClient.get<NomConfig>(uri).subscribe({
      next: (response) => {
        this._config = response;

        this.settingsLoaded.next();
      },
      error: (error) => {},
    });
  }
}
