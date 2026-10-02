import { cleanup } from "@testing-library/react";
import { afterEach, beforeEach, vi } from "vite-plus/test";
import { toApiError } from "@/core/apis/api-error";
import { client } from "@/core/apis/generated/client.gen";
import { fixtureNow } from "./units/fixtures";

// Same typed failures as the application (configureApiClient), without the sign-in.
client.interceptors.error.use(toApiError);

// A frozen clock: only Date is replaced, the timers stay real for MSW, TanStack Query and Testing Library.
beforeEach(() => vi.setSystemTime(fixtureNow));

afterEach(() => {
	cleanup();
	vi.useRealTimers();
});
