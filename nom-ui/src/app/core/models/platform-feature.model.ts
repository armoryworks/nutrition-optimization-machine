/** A platform-wide feature switch (kill switch for a whole subsystem). */
export interface PlatformFeatureModel {
  key: string;
  isEnabled: boolean;
  description?: string | null;
  lastModifiedDate?: string | null;
}
