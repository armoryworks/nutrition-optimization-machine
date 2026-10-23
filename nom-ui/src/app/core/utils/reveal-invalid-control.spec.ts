import { FormControl, FormGroup, Validators } from '@angular/forms';
import { findFirstInvalidElement, revealFirstInvalid } from './reveal-invalid-control';

describe('revealFirstInvalid', () => {
  let host: HTMLElement;
  let form: FormGroup;

  function field(name: string, invalid: boolean): { wrapper: HTMLElement; input: HTMLInputElement } {
    const wrapper = document.createElement('mat-form-field');
    const input = document.createElement('input');
    input.setAttribute('formcontrolname', name);
    if (invalid) input.classList.add('ng-invalid');
    input.scrollIntoView = vi.fn();
    wrapper.scrollIntoView = vi.fn();
    wrapper.appendChild(input);
    host.appendChild(wrapper);
    return { wrapper, input };
  }

  beforeEach(() => {
    vi.useFakeTimers();
    host = document.createElement('form');
    host.classList.add('ng-invalid');
    document.body.appendChild(host);
    form = new FormGroup({
      name: new FormControl('ok'),
      age: new FormControl(null, Validators.required),
      city: new FormControl(null, Validators.required),
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    host.remove();
  });

  it('marks every control touched', () => {
    revealFirstInvalid(form, host);
    expect(form.get('name')?.touched).toBe(true);
    expect(form.get('age')?.touched).toBe(true);
  });

  it('scrolls the first invalid field near the top and focuses its input', () => {
    field('name', false);
    const age = field('age', true);
    const city = field('city', true);

    revealFirstInvalid(form, host);
    vi.runAllTimers();

    expect(age.wrapper.scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' });
    expect(city.wrapper.scrollIntoView).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(age.input);
  });

  it('runs the expand callback before looking for the control', () => {
    const expand = vi.fn(() => field('age', true));

    revealFirstInvalid(form, host, { expand });
    vi.runAllTimers();

    expect(expand).toHaveBeenCalledTimes(1);
    expect(document.activeElement?.getAttribute('formcontrolname')).toBe('age');
  });

  it('does nothing visible when no invalid control is rendered', () => {
    const ok = field('name', false);

    revealFirstInvalid(form, host);
    vi.runAllTimers();

    expect(ok.wrapper.scrollIntoView).not.toHaveBeenCalled();
    expect(findFirstInvalidElement(host)).toBeNull();
  });

  it('ignores controls of another form nested inside the host', () => {
    const nested = document.createElement('form');
    const foreign = document.createElement('input');
    foreign.classList.add('ng-invalid');
    foreign.scrollIntoView = vi.fn();
    nested.appendChild(foreign);
    host.appendChild(nested);
    const age = field('age', true);

    revealFirstInvalid(form, host);
    vi.runAllTimers();

    expect(foreign.scrollIntoView).not.toHaveBeenCalled();
    expect(age.wrapper.scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' });
    expect(document.activeElement).toBe(age.input);
  });

  it('scopes to the first form inside a component host', () => {
    const component = document.createElement('nom-thing');
    const own = document.createElement('form');
    const input = document.createElement('input');
    input.classList.add('ng-invalid');
    own.appendChild(input);
    component.appendChild(own);
    const sibling = document.createElement('form');
    const other = document.createElement('input');
    other.classList.add('ng-invalid');
    sibling.appendChild(other);
    component.appendChild(sibling);
    document.body.appendChild(component);

    expect(findFirstInvalidElement(component)).toBe(input);
    component.remove();
  });

  it('falls back to the innermost invalid group when the invalid control has no element', () => {
    const row = document.createElement('div');
    row.setAttribute('formgroupname', '0');
    row.classList.add('ng-invalid');
    row.scrollIntoView = vi.fn();
    const visible = document.createElement('input');
    visible.setAttribute('formcontrolname', 'searchText');
    row.appendChild(visible);
    host.appendChild(row);

    revealFirstInvalid(form, host);
    vi.runAllTimers();

    expect(findFirstInvalidElement(host)).toBe(row);
    expect(row.scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' });
    expect(document.activeElement).toBe(visible);
  });

  it('tolerates a missing host', () => {
    expect(() => revealFirstInvalid(form, null)).not.toThrow();
    expect(form.get('age')?.touched).toBe(true);
  });
});
