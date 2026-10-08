/** A platform-wide feature switch (kill switch for a whole subsystem). */
export interface PlatformFeatureModel {
  key: string;
  label?: string | null;
  isEnabled: boolean;
  description?: string | null;
  createdDate?: string | null;
  createdByName?: string | null;
  lastModifiedDate?: string | null;
  lastModifiedByName?: string | null;
}
