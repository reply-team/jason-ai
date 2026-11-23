import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { route } from '../../router';
import { FakeApi } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';

describe('the Chat screen', () => {
    it('is the Chat row of the navigation', async () => {
        renderApp(new FakeApi());
        route.value = { screen: 'chat' };
        await waitFor(() => expect(screen.getByRole('link', { name: 'Chat' }).getAttribute('href')).toBe('#/chat'));
    });
});
