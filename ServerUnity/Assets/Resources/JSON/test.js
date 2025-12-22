// === xvis-config.js ===
// Global configuration for multi-device scatter experiment.
// Load this BEFORE desktop/mobile/spatial/controller scripts.

(() => {
  // If already defined (e.g., hot reload), do not clobber.
  if (window.Config) return;

  window.Config = {
    // --- spatial config ---
    spatial: {
      server: {
        registration_ws_url: "wss://10.0.0.250:4001/register",
      },
      tracker: {
        desktop1: "DesktopImageTarget",
        mobile1: "MobileIRTracker",
        tab1: "TabIRTracker",
      },
      event: {
        toggleVisibleY: {
          id: "cornerContactTRtoBL",
          streamKey: null,
        },
        toggleStretchX: {
          id: "cornerContactTLtoBR",
          streamKey: null,
        },
        stretchY: {
          id: "parallelSurfaceDistantAbove",
          streamKey: "primitives.distantAbove.measurement",
        },
        stretchXZ: {
          id: "parallelSurfaceDistantRightBcornerTLtoBR",
          streamKey: "primitives.distantRightBcornerTLtoBR.measurement",
        },
        offloadFilterUI: {
          id: "lateralEdgeContact_SideBySide",
          streamKey: null,
        },
        decouple: {
          id: "generalProximity_AB",
          streamKey: null,
        },
        fadeFromProximity: {
          id: "rightSideProximity",
          streamKey: "primitives.proximateLateralEdge.measurement",
        },
        speedFromTilt: {
          id: "rightSideTouchLateralTilt",
          streamKey: "primitives.lateralTilt.measurement",
        },
      },
    },
    // --- view config ---
    view: {
      scatter3d: {
        scatter3d_ws_url: "wss://10.0.0.250:4001/scatter",
      },
      defaults: {
        axis3d: {
          xLen: 0.22,
          yLen: 0.05,
          zLen: 0.11,
        },
      },
      signal: {
        fadeUpdate: "fade:update",
        speedUpdate: "speed:update",

        // Offload / decouple mobile filter
        offloadFilter: "offload:filter",
        decouple: "decouple",

        // Country selection sync
        countriesUpdate: "countries:update",
        countriesRequest: "countries:request",
        countriesState: "countries:state",

        // Layout-related
        layoutChanged: "layout:changed",
      },
    },

    // --- Data config ---
    data: {
      gapminderJsonUrl:
        "https://cdn.jsdelivr.net/npm/vega-datasets@3/data/gapminder.json",
    },
  };

  console.log("[Config] Loaded", window.Config);
})();
