import { createContext, useContext } from 'react';
import type { Html } from '../../console/html';

/**
 * The provider's accounts as the desktop draws and acts on them: the card the Connect panel and a chat show, painted
 * from `view`, and `gone` told when the page stops painting it so a dialog it held open is dropped. The desktop's console
 * hands it to a page it shows; a browser hands none, and then the page says that the acts are the desktop's.
 */
export interface ProviderAccounts {
    view: () => Html;
    gone: () => void;
}

export const ProviderAccountsContext = createContext<ProviderAccounts | null>(null);

export function useProviderAccounts(): ProviderAccounts | null {
    return useContext(ProviderAccountsContext);
}
