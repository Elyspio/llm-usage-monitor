import { defineConfig } from "vite-plus";

export default defineConfig({
	fmt: {
		indent: 4,
		useTabs: true,
	},
	lint: {
		options: {
			typeAware: true,
			typeCheck: true,
		},
	},
});
