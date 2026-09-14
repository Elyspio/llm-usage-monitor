import type { User } from "oidc-client-ts";
import { createContext, type ReactNode, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { userManager } from "@/core/auth/user-manager";

type AuthContextValue = {
	/** Signed-in user, or `null` when there is no valid session. */
	user: User | null;
	/** `true` until the stored session has been read. */
	loading: boolean;
	signIn: () => void;
	signOut: () => void;
	/** Exchanges the authorization code of the sign-in callback URL. */
	completeSignIn: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export const AuthProvider = ({ children }: { children: ReactNode }) => {
	const [user, setUser] = useState<User | null>(null);
	const [loading, setLoading] = useState(true);

	useEffect(() => {
		void userManager.getUser().then((stored) => {
			setUser(stored && !stored.expired ? stored : null);
			setLoading(false);
		});

		const onUserLoaded = (loaded: User) => setUser(loaded);
		const onUserUnloaded = () => setUser(null);
		userManager.events.addUserLoaded(onUserLoaded);
		userManager.events.addUserUnloaded(onUserUnloaded);

		return () => {
			userManager.events.removeUserLoaded(onUserLoaded);
			userManager.events.removeUserUnloaded(onUserUnloaded);
		};
	}, []);

	const completeSignIn = useCallback(async () => {
		const signedIn = await userManager.signinRedirectCallback();
		setUser(signedIn.expired ? null : signedIn);
	}, []);

	const value = useMemo<AuthContextValue>(
		() => ({
			user,
			loading,
			signIn: () => void userManager.signinRedirect(),
			signOut: () => void userManager.signoutRedirect(),
			completeSignIn,
		}),
		[user, loading, completeSignIn]
	);

	return <AuthContext value={value}>{children}</AuthContext>;
};

export const useAuth = () => {
	const context = useContext(AuthContext);
	if (!context) throw new Error("useAuth must be used within AuthProvider");
	return context;
};
