# TravelTracker frontend

Independent React + TypeScript + Vite application for #32. Requires Node 22.12+ (verified with Node 24) and npm. Run from this folder:

```powershell
npm ci
npm run dev
npm run build
npm test
```

Open the address printed by Vite (normally http://127.0.0.1:5173). No backend is needed to navigate. The header menu opens Service status, which makes a real anonymous GET /health request. Start Attractions on 5104 or Transport on 5105 to see success; stop the service and retry to see an unavailable response. An unreachable direct browser endpoint produces the connection error state. Requests time out after eight seconds and cancel on navigation.

Copy `.env.example` to `.env.local` to override service locations. `VITE_*` values are public build-time configuration, never secrets. Development proxies avoid browser CORS restrictions. Production hosting must route `/services/attractions/*` and `/services/transport/*` to the corresponding services, stripping the prefix, or build with absolute VITE URLs and configure narrowly scoped CORS on those services. Vite preview serves the build only; it is not a production API proxy.

The Figma FianlView canvas supplies the application shell (92:3466), reusable form (92:3488), and feedback styling (92:3462). Original logo/account/menu SVGs are local assets. Fonts ship with the app through Fontsource. The 1920px reference layout adapts to smaller displays. Hash navigation supports static hosting, back/forward, labelled controls, skip navigation and page-heading focus.

Auth0 sign-in, hosted logout and protected HTTP access are implemented through the official SDK. Follow the [Auth0 setup and verification guide](../docs/frontend-auth0.md) for tenant registration, public settings and backend configuration. Real-provider acceptance is pending configuration; no credentials or tokens are committed.

New Trip demonstrates the reusable required-field form and explicitly reports that nothing was saved. History and Settings are placeholders. Catalogue administration, trip persistence and maps remain separate issues. No database code, backend assemblies or fabricated trip data are included.
