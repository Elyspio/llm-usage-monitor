import { cleanup } from "@testing-library/react";
import { afterEach } from "vite-plus/test";
import { toApiError } from "@/core/apis/api-error";
import { client } from "@/core/apis/generated/client.gen";

// Same typed failures as the application (configureApiClient), without the sign-in.
client.interceptors.error.use(toApiError);

afterEach(() => cleanup());
