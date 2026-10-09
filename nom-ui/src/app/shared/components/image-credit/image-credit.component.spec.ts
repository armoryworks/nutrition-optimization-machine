import { TestBed } from '@angular/core/testing';
import { ImageCredit } from './image-credit.component';
import { RecipeImageCredit } from '../../../core/models/recipe-image.model';

describe('ImageCredit', () => {
  function render(credit: RecipeImageCredit): HTMLElement {
    TestBed.configureTestingModule({ imports: [ImageCredit] });
    const fixture = TestBed.createComponent(ImageCredit);
    fixture.componentRef.setInput('credit', credit);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('credits the author, the source and the licence with links', () => {
    const el = render({
      author: 'Jane Cook',
      authorUrl: 'https://commons.wikimedia.org/wiki/User:Jane',
      sourceName: 'Wikimedia Commons',
      sourceUrl: 'https://commons.wikimedia.org/wiki/File:Soup.jpg',
      license: 'CC BY 4.0',
      licenseUrl: 'https://creativecommons.org/licenses/by/4.0',
      noDerivatives: false,
    });

    expect(el.textContent?.replace(/\s+/g, ' ').trim()).toBe('Photo by Jane Cook on Wikimedia Commons · CC BY 4.0');
    const author = el.querySelector<HTMLAnchorElement>('[data-testid="recipe-image-credit-author-link"]')!;
    expect(author.href).toBe('https://commons.wikimedia.org/wiki/User:Jane');
    expect(author.rel).toContain('noopener');
    expect(author.target).toBe('_blank');
    expect(el.querySelector<HTMLAnchorElement>('[data-testid="recipe-image-credit-source-link"]')!.href).toContain('File:Soup.jpg');
    expect(el.querySelector<HTMLAnchorElement>('[data-testid="recipe-image-credit-license-link"]')!.href).toContain('creativecommons.org');
  });

  it('falls back to plain text when the source gave no links', () => {
    const el = render({ author: 'Sam', sourceName: 'Pixabay', license: 'Pixabay Content License', noDerivatives: false });

    expect(el.querySelectorAll('a').length).toBe(0);
    expect(el.querySelector('[data-testid="recipe-image-credit-author"]')!.textContent).toBe('Sam');
    expect(el.querySelector('[data-testid="recipe-image-credit-license"]')!.textContent).toBe('Pixabay Content License');
  });

  it('omits the author clause for public-domain photos with no named creator', () => {
    const el = render({ sourceName: 'Openverse', sourceUrl: 'https://example.org/p', license: 'Public Domain Mark 1.0', noDerivatives: false });

    expect(el.textContent?.replace(/\s+/g, ' ').trim()).toBe('Photo on Openverse · Public Domain Mark 1.0');
  });
});
