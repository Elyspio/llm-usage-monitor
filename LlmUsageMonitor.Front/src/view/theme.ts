import { createTheme } from "@mui/material";
import "@fontsource/manrope/latin-400.css";
import "@fontsource/manrope/latin-600.css";
import "@fontsource/manrope/latin-700.css";
import "@fontsource/ibm-plex-mono/latin-500.css";

export const theme = createTheme({
	cssVariables: true,
	palette: {
		mode: "dark",
		primary: { main: "#10b981", contrastText: "#052e23" },
		secondary: { main: "#a1a1aa" },
		success: { main: "#10b981" },
		warning: { main: "#fbbf24" },
		error: { main: "#ff9292" },
		background: { default: "#0a0a0b", paper: "#18181b" },
		text: { primary: "#fafafa", secondary: "#a1a1aa" },
		divider: "#29292d",
	},
	shape: { borderRadius: 14 },
	typography: {
		fontFamily: '"Manrope", sans-serif',
		h1: { fontSize: "2.75rem", fontWeight: 600, letterSpacing: "-0.045em" },
		h5: { fontSize: "2rem", fontWeight: 600, letterSpacing: "-0.04em" },
		h6: { fontSize: "1.12rem", fontWeight: 700, letterSpacing: "-0.025em" },
		body2: { fontSize: "0.8rem", lineHeight: 1.7 },
		caption: { fontSize: "0.7rem", lineHeight: 1.6 },
		button: { textTransform: "none", fontWeight: 700, fontSize: "0.78rem" },
		overline: { fontFamily: '"IBM Plex Mono", monospace', fontSize: "0.75rem", letterSpacing: "0.1em" },
	},
	components: {
		MuiCssBaseline: {
			styleOverrides: {
				body: { minWidth: 320 },
				"::selection": { background: "#10b981", color: "#ffffff" },
				"*:focus-visible": { outline: "2px solid #10b981", outlineOffset: 4 },
				"@media (prefers-reduced-motion: reduce)": { "*, *::before, *::after": { animation: "none !important", transition: "none !important" } },
			},
		},
		MuiPaper: { styleOverrides: { root: { backgroundImage: "none" } } },
		MuiButton: { defaultProps: { disableElevation: true }, styleOverrides: { root: { borderRadius: 8, padding: "9px 14px" } } },
		MuiChip: { styleOverrides: { root: { borderRadius: 6, fontSize: "0.65rem", fontWeight: 600 }, sizeSmall: { height: 23 } } },
		MuiTableCell: {
			styleOverrides: {
				root: { borderColor: "#29292d", padding: "14px 10px", fontSize: "0.75rem" },
				head: { color: "#a1a1aa", fontSize: "0.65rem", textTransform: "uppercase", letterSpacing: "0.08em" },
			},
		},
		MuiTextField: { defaultProps: { size: "small" } },
		MuiSwitch: { defaultProps: { size: "small" } },
		MuiOutlinedInput: { styleOverrides: { root: { borderRadius: 8 } } },
		MuiToggleButton: { styleOverrides: { root: { borderRadius: 7, padding: "5px 14px" } } },
	},
});
