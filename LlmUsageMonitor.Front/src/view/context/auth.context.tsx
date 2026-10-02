import type { User } from "oidc-client-ts";
import { createContext, type ReactNode, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { onAuthFailure } from "@/core/apis/api-error";
import { currentReturnPath, returnPathOf, type SignInState } from "@/core/auth/return-path";
import { userManager } from "@/core/auth/user-manager";

type AuthContextValue = {
	/** Signed-in user, or `null` when there is no valid session. */
	user: User | null;
	/** `true` until the stored session has been read. */
	loading: boolean;
	/** The session ended on its own (token expired, 401): the sign-in screen says so. */
	sessionExpired: boolean;
	/** The API answered 403: the account is signed in but lacks the role. */
	accessDenied: boolean;
	/** Redirects to Keycloak; the current page is given back after the sign-in. */
	signIn: () => void;
	signOut: () => void;
	/** Exchanges the authorization code of the sign-in callback URL; resolves with the page asked before the sign-in. */
	completeSignIn: () => Promise<string>;
};

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export const AuthProvider = ({ children }: { children: ReactNode }) => {
	const [user, setUser] = useState<User | null>(null);
	const [loading, setLoading] = useState(true);
	const [sessionExpired, setSessionExpired] = useState(false);
	const [accessDenied, setAccessDenied] = useState(false);

	useEffect(() => {
		void userManager.getUser().then((stored) => {
			setUser(stored && !stored.expired ? stored : null);
			setSessionExpired(Boolean(stored?.expired));
			setLoading(false);
		});

		/** Back to the sign-in screen: the stored user goes, so no request leaves with a dead token. */
		const expire = () => {
			setSessionExpired(true);
			setUser(null);
			void userManager.removeUser();
		};
		const onUserLoaded = (loaded: User) => {
			setUser(loaded);
			setSessionExpired(false);
			setAccessDenied(false);
		};
		const onUserUnloaded = () => setUser(null);
		userManager.events.addUserLoaded(onUserLoaded);
		userManager.events.addUserUnloaded(onUserUnloaded);
		// A failed silent renewal alone keeps the session: the error may be transient and the token still valid. The session
		// ends when the token actually expires (no renewal succeeded until then) or when the API answers 401.
		userManager.events.addAccessTokenExpired(expire);
		const stopListening = onAuthFailure((status) => (status === 401 ? expire() : setAccessDenied(true)));

		return () => {
			userManager.events.removeUserLoaded(onUserLoaded);
			userManager.events.removeUserUnloaded(onUserUnloaded);
			userManager.events.removeAccessTokenExpired(expire);
			stopListening();
		};
	}, []);

	const completeSignIn = useCallback(async () => {
		const signedIn = await userManager.signinRedirectCallback();
		setUser(signedIn.expired ? null : signedIn);
		setSessionExpired(false);
		setAccessDenied(false);
		return returnPathOf(signedIn.state);
	}, []);

	const value = useMemo<AuthContextValue>(
		() => ({
			user,
			loading,
			sessionExpired,
			accessDenied,
			signIn: () => void userManager.signinRedirect({ state: { returnTo: currentReturnPath(window.location) } satisfies SignInState }),
			signOut: () => void userManager.signoutRedirect(),
			completeSignIn,
		}),
		[user, loading, sessionExpired, accessDenied, completeSignIn]
	);

	return <AuthContext value={value}>{children}</AuthContext>;
};

export const useAuth = () => {
	const context = useContext(AuthContext);
	if (!context) throw new Error("useAuth must be used within AuthProvider");
	return context;
};
