// Per-instance runtime config for nom-ui — the admin-controlled knobs.
// Values here are the hosted nom.nommeal.com defaults; other instances
// replace this file at deploy time (volume-mount or sed), same pattern as
// nommeal.com's config.js.
window.NOM_UI_CONFIG = {
  // Where the header brand links for logged-OUT visitors (the marketing
  // site fronting this instance). Logged-in users always go to their own
  // dashboard. Empty string = brand always navigates in-app.
  marketingSite: "https://nommeal.com",

  // Instance-specific terms of service. When set, the footer Terms link goes
  // here instead of the app's built-in generic /terms page — hosted
  // deployments publish their own richer terms and point this at them.
  termsUrl: "https://nommeal.com/terms/",
};
