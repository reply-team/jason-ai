import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { ScrollBox } from './scroll-box';

// The box around a table is a named region in the keyboard's order only while the table is wider than the room.

const sizes = (scrollWidth: number, clientWidth: number) => {
    Object.defineProperty(HTMLElement.prototype, 'scrollWidth', { configurable: true, get: () => scrollWidth });
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => clientWidth });
};

afterEach(() => {
    cleanup();
    Reflect.deleteProperty(HTMLElement.prototype, 'scrollWidth');
    Reflect.deleteProperty(HTMLElement.prototype, 'clientWidth');
});

describe('the box around a table', () => {
    it('is a plain box where the table fits, so the keyboard order is as it was', async () => {
        sizes(300, 300);
        const { container } = render(
            <ScrollBox label="Agents">
                <table />
            </ScrollBox>,
        );
        await act(async () => undefined);
        const box = container.firstElementChild!;
        expect(box.getAttribute('tabindex')).toBeNull();
        expect(box.getAttribute('role')).toBeNull();
        expect(screen.queryByRole('region')).toBeNull();
    });

    it('is a named region the keyboard reaches where the table is wider than the room', async () => {
        sizes(500, 300);
        render(
            <ScrollBox label="Agents">
                <table />
            </ScrollBox>,
        );
        await act(async () => undefined);
        const box = screen.getByRole('region', { name: 'Agents' });
        expect(box.getAttribute('tabindex')).toBe('0');
        expect(box.classList.contains('tablewrap')).toBe(true);
    });
});
