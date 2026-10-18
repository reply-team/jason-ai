import { describe, expect, it } from 'vitest';
import { mergePage } from './paging';

const items = (...ids: string[]) => ids.map((id) => ({ id }));

describe('mergePage', () => {
    it.each([
        [items('a', 'b'), items('c'), ['a', 'b', 'c']],
        [items('a', 'b'), items('b', 'c'), ['a', 'b', 'c']],
        [items(), items('a'), ['a']],
        [items('a'), items(), ['a']],
        [items('a', 'b'), items('a', 'b'), ['a', 'b']],
    ])('appends what is new and never shows an item twice', (existing, next, expected) => {
        expect(mergePage(existing, next, (i) => i.id).map((i) => i.id)).toEqual(expected);
    });

    it('keeps the newer copy of an item seen again', () => {
        const merged = mergePage([{ id: 'a', v: 1 }], [{ id: 'a', v: 2 }], (i) => i.id);
        expect(merged).toEqual([{ id: 'a', v: 2 }]);
    });
});
