import { createTheme } from "@mui/material";
import "@fontsource/manrope/latin-400.css";
import "@fontsource/manrope/latin-600.css";
import "@fontsource/manrope/latin-700.css";
import "@fontsource/ibm-plex-mono/latin-500.css";

declare module "@mui/material/styles" {
	/** Colours of the application outside the MUI palette, read in `sx` as `surface.<key>`. */
	interface SurfacePalette {
		/** Background of the logo, the avatar and the section icons. */
		accent: string;
		/** Background of the active navigation link. */
		selected: string;
		/** Track of the quota bars. */
		track: string;
		/** Window whose values are stale or whose reset has passed. */
		muted: string;
		/** "Now" marker of the timeline. */
		now: string;
		/** Monday markers of the timeline. */
		monday: string;
	}

	interface Palette {
		surface: SurfacePalette;
	}

	interface PaletteOptions {
		surface?: SurfacePalette;
	}
}

const primary = "#10b981";
const divider = "#29292d";
const textSecondary = "#a1a1aa";

export const theme = createTheme({
	cssVariables: true,
	palette: {
		mode: "dark",
		primary: { main: primary, contrastText: "#052e23" },
		secondary: { main: textSecondary },
		success: { main: primary },
		warning: { main: "#fbbf24" },
		error: { main: "#ff9292" },
		background: { default: "#0a0a0b", paper: "#18181b" },
		text: { primary: "#fafafa", secondary: textSecondary },
		divider,
		surface: { accent: "#12352b", selected: "#15352d", track: "#28282b", muted: "#85858e", now: "#d4d4d8", monday: "#7dd3fc" },
	},
	shape: { borderRadius: 14 },
	typography: {
		fontFamily: '"Manrope", sans-serif',
		h1: { fontSize: "2.75rem", fontWeight: 600, letterSpacing: "-0.045em" },
		h5: { fontSize: "2rem", fontWeight: 600, letterSpacing: "-0.04em" },
		h6: { fontSize: "1.12rem", fontWeight: 700, letterSpacing: "-0.025em" },
		body2: { fontSize: "0.8rem", lineHeight: 1.7 },
		// 0.75rem is the smallest text of the application.
		caption: { fontSize: "0.75rem", lineHeight: 1.6 },
		button: { textTransform: "none", fontWeight: 700, fontSize: "0.78rem" },
		overline: { fontFamily: '"IBM Plex Mono", monospace', fontSize: "0.75rem", letterSpacing: "0.1em" },
	},
	components: {
		MuiCssBaseline: {
			styleOverrides: {
				body: { minWidth: 320 },
				"::selection": { background: primary, color: "#ffffff" },
				"*:focus-visible": { outline: `2px solid ${primary}`, outlineOffset: 4 },
				"@media (prefers-reduced-motion: reduce)": { "*, *::before, *::after": { animation: "none !important", transition: "none !important" } },
			},
		},
		MuiPaper: { styleOverrides: { root: { backgroundImage: "none" } } },
		MuiButton: { defaultProps: { disableElevation: true }, styleOverrides: { root: { borderRadius: 8, padding: "9px 14px" } } },
		MuiChip: { styleOverrides: { root: { borderRadius: 6, fontSize: "0.75rem", fontWeight: 600 }, sizeSmall: { height: 23 } } },
		MuiTableCell: {
			styleOverrides: {
				root: { borderColor: divider, padding: "14px 10px", fontSize: "0.75rem" },
				head: { color: textSecondary, fontSize: "0.75rem", textTransform: "uppercase", letterSpacing: "0.08em" },
			},
		},
		MuiTextField: { defaultProps: { size: "small" } },
		MuiSwitch: { defaultProps: { size: "small" } },
		MuiOutlinedInput: { styleOverrides: { root: { borderRadius: 8 } } },
		MuiToggleButton: { styleOverrides: { root: { borderRadius: 7, padding: "5px 14px" } } },
	},
});

/** Read by screen readers, invisible on screen: the text alternatives of the charts. */
export const visuallyHidden = {
	position: "absolute",
	width: 1,
	height: 1,
	margin: -1,
	padding: 0,
	border: 0,
	overflow: "hidden",
	clip: "rect(0 0 0 0)",
	whiteSpace: "nowrap",
} as const;
