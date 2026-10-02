import { useQuery } from "@tanstack/react-query";
import { getDashboardOptions } from "@/core/apis/generated/@tanstack/react-query.gen";

/** The dashboard snapshot shared by the Dashboard and History pages: one cache entry, refreshed every 30 s and on focus. */
export const useDashboard = () => useQuery({ ...getDashboardOptions(), refetchInterval: 30_000, refetchOnWindowFocus: true });
