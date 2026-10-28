# React + TypeScript + Vite

This template provides a minimal setup to get React working in Vite with HMR and some ESLint rules.

Currently, two official plugins are available:

- [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react/README.md) uses [Babel](https://babeljs.io/) for Fast Refresh
- [@vitejs/plugin-react-swc](https://github.com/vitejs/vite-plugin-react-swc) uses [SWC](https://swc.rs/) for Fast Refresh

## Expanding the ESLint configuration

If you are developing a production application, we recommend updating the configuration to enable type-aware lint rules:

```js
export default tseslint.config({
  extends: [
    // Remove ...tseslint.configs.recommended and replace with this
    ...tseslint.configs.recommendedTypeChecked,
    // Alternatively, use this for stricter rules
    ...tseslint.configs.strictTypeChecked,
    // Optionally, add this for stylistic rules
    ...tseslint.configs.stylisticTypeChecked,
  ],
  languageOptions: {
    // other options...
    parserOptions: {
      project: ['./tsconfig.node.json', './tsconfig.app.json'],
      tsconfigRootDir: import.meta.dirname,
    },
  },
})
```

You can also install [eslint-plugin-react-x](https://github.com/Rel1cx/eslint-react/tree/main/packages/plugins/eslint-plugin-react-x) and [eslint-plugin-react-dom](https://github.com/Rel1cx/eslint-react/tree/main/packages/plugins/eslint-plugin-react-dom) for React-specific lint rules:

```js
// eslint.config.js
import reactX from 'eslint-plugin-react-x'
import reactDom from 'eslint-plugin-react-dom'

export default tseslint.config({
  plugins: {
    // Add the react-x and react-dom plugins
    'react-x': reactX,
    'react-dom': reactDom,
  },
  rules: {
    // other rules...
    // Enable its recommended typescript rules
    ...reactX.configs['recommended-typescript'].rules,
    ...reactDom.configs.recommended.rules,
  },
})
```
# Local Mapbox setup

Copy `.env.example` to the ignored `.env.local` file and set `VITE_MAPBOX_ACCESS_TOKEN` to a public browser token. Restrict it to your local preview origin (for this setup, `http://localhost:57082`) and only the scopes needed by Mapbox GL JS. The token is intentionally sent from the browser; never commit it, paste it into issues/reports, or include it in screenshots. Mapbox usage is metered, and any free allowance is not a spending cap. This client does not change account, billing, or quota settings.

Mapbox GL JS renders the location map. Address and venue search is currently unavailable: Search Box results are temporary-use only, and permanent Geocoding is disabled until account eligibility and a server-side permanent-storage setting are confirmed. Do not persist or relabel Search Box results. Users can enter their own location label and place a pin on the map; durable locations use `locationProvenance: userProvided`. A future eligible Geocoding v6 path may use `locationProvenance: mapboxGeocodingPermanent`, but a browser flag cannot enable it.

The map only confirms locations; it does not draw driving routes. Planner distances remain Haversine straight-line estimates, and arrival time remains coordination information rather than a guarantee.
