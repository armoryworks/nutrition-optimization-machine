import { TestCredentials } from '../support/commands';
import { selectedViewports } from '../support/viewports';

interface AdminSection {
  id: string;
  navTestId: string;
  rootTestId: string;
  readyTestIds: string[];
}

const CURATED_STATUS_ID = 9003;

const sections: AdminSection[] = [
  { id: 'users', navTestId: 'nav-admin-users', rootTestId: 'users-admin', readyTestIds: ['users-admin-list', 'users-admin-empty'] },
  { id: 'clients', navTestId: 'nav-admin-clients', rootTestId: 'clients-admin', readyTestIds: ['clients-admin-list', 'clients-admin-empty'] },
  { id: 'curation', navTestId: 'nav-admin-curation', rootTestId: 'curation-queue', readyTestIds: ['curation-list', 'curation-empty'] },
  { id: 'food-catalog', navTestId: 'nav-food-catalog', rootTestId: 'food-catalog', readyTestIds: ['food-catalog-list', 'food-catalog-empty'] },
  { id: 'scraping-sources', navTestId: 'nav-scraping-sources', rootTestId: 'admin-scraping-sources', readyTestIds: ['admin-scraping-sources-list', 'admin-scraping-sources-empty'] },
  { id: 'kitchen-tools', navTestId: 'nav-admin-kitchen-tools', rootTestId: 'admin-kitchen-tools', readyTestIds: ['kitchen-tools-admin-list'] },
  { id: 'recipe-images', navTestId: 'nav-admin-recipe-images', rootTestId: 'admin-recipe-images', readyTestIds: ['recipe-images-admin-list'] },
  { id: 'diet-categories', navTestId: 'nav-diet-categories', rootTestId: 'diet-categories', readyTestIds: ['diet-categories-list'] },
  { id: 'platform-features', navTestId: 'nav-platform-features', rootTestId: 'platform-features', readyTestIds: ['platform-features-list', 'platform-features-empty'] },
  { id: 'webhooks', navTestId: 'nav-admin-webhooks', rootTestId: 'webhooks', readyTestIds: ['webhooks-list', 'webhooks-empty'] },
];

const anyOf = (ids: string[]) => ids.map((id) => `[data-testid="${id}"]`).join(', ');

const openSection = (section: AdminSection) => {
  cy.visit('/admin');
  cy.byTestId('admin-tab-bar').should('be.visible');
  cy.byTestId(section.navTestId).click();
  cy.location('pathname').should('eq', `/admin/${section.id}`);
  cy.byTestId(section.navTestId).should('have.attr', 'aria-current', 'page');
  cy.byTestId('admin-panel').within(() => {
    cy.byTestId(section.rootTestId).should('be.visible');
    cy.get(anyOf(section.readyTestIds)).should('exist');
  });
};

const openFoodCatalog = () => {
  cy.intercept('GET', '**/api/FoodCatalog?*').as('catalogPage');
  cy.visit('/admin/food-catalog');
  cy.byTestId('food-catalog').should('be.visible');
  cy.wait('@catalogPage').its('response.statusCode').should('eq', 200);
};

const showAllSources = () => {
  cy.byTestId('food-catalog-source').click();
  cy.byTestId('food-catalog-source-option-all').click();
  cy.wait('@catalogPage').its('response.statusCode').should('eq', 200);
  cy.byTestId('food-catalog-list').should('be.visible');
};

const adminJourney = (viewportName: 'desktop' | 'phone', viewport: { width: number; height: number }) => {
  const isPhone = viewportName === 'phone';

  describe(`Admin sections (${viewportName})`, () => {
    let admin: TestCredentials;

    before(() => {
      cy.viewport(viewport.width, viewport.height);
      cy.registerAdmin(`admin-sections-${viewportName}`).then((credentials) => {
        admin = credentials;
      });
    });

    beforeEach(() => {
      cy.viewport(viewport.width, viewport.height);
      cy.session(['admin-sections', viewportName, admin.email], () => cy.loginAs(admin.email, admin.password), {
        validate: () => {
          cy.window().its('localStorage.authToken').should('be.a', 'string').and('not.be.empty');
        },
      });
    });

    it('shows the overview launchpad with a card for every section', () => {
      cy.visit('/admin');
      cy.byTestId('admin').should('be.visible');
      cy.byTestId('admin-tab-bar').should('be.visible');
      cy.byTestId('admin-panel').should('be.visible');
      cy.byTestId('nav-admin-overview').should('have.attr', 'aria-current', 'page');

      for (const section of sections) {
        cy.byTestId(`admin-card-${section.id}`).scrollIntoView().should('be.visible');
      }

      if (isPhone) {
        cy.byTestId('header-nav-toggle-btn').should('be.visible');
        cy.byTestId('admin-tab-bar')
          .should('have.css', 'flex-direction', 'row')
          .and('have.css', 'overflow-x', 'auto')
          .then(($nav) => {
            expect($nav[0].scrollWidth, 'admin menu strip scrolls sideways').to.be.greaterThan($nav[0].clientWidth);
          });
      } else {
        cy.byTestId('header-nav-toggle-btn').should('not.be.visible');
        cy.byTestId('admin-tab-bar')
          .should('have.css', 'flex-direction', 'column')
          .and('have.css', 'position', 'sticky');
      }
    });

    it('reaches the admin hub from the main navigation', () => {
      cy.visit('/home');
      cy.navTo('nav-admin');
      cy.location('pathname').should('eq', '/admin');
      cy.byTestId('admin').should('be.visible');
      cy.byTestId('admin-card-curation').scrollIntoView().should('be.visible');
    });

    it('opens every section from an overview card', () => {
      cy.visit('/admin');
      for (const section of sections) {
        cy.byTestId('nav-admin-overview').click();
        cy.location('pathname').should('eq', '/admin');
        cy.byTestId(`admin-card-${section.id}`).scrollIntoView().click();
        cy.location('pathname').should('eq', `/admin/${section.id}`);
        cy.byTestId(section.rootTestId).should('be.visible');
      }
    });

    for (const section of sections) {
      it(`opens ${section.id} from the admin menu`, () => {
        openSection(section);

        if (section.id === 'kitchen-tools') {
          cy.byTestId('kitchen-tools-admin-filter-pending').should('have.attr', 'aria-pressed', 'true');
          cy.byTestId('kitchen-tools-admin-filter-tools').click();
          cy.byTestId('kitchen-tools-admin-filter-tools').should('have.attr', 'aria-pressed', 'true');
          cy.byTestId('kitchen-tools-admin-filter-pending').should('have.attr', 'aria-pressed', 'false');
          cy.byTestId('kitchen-tools-admin-tools-list').should('exist');
          cy.byTestId('kitchen-tools-admin-empty').should('not.exist');
        }

        if (isPhone && section.id === 'webhooks') {
          cy.byTestId('admin-tab-bar').should(($nav) => {
            expect($nav[0].scrollLeft, 'menu strip scrolled to the active section').to.be.greaterThan(0);
          });
        }
      });
    }

    it('falls back to the overview for an unknown section', () => {
      cy.visit('/admin/not-a-section');
      cy.byTestId('admin').should('be.visible');
      cy.byTestId('nav-admin-overview').should('have.attr', 'aria-current', 'page');
      cy.byTestId('admin-card-users').should('exist');
    });

    it('food catalog: approve for planning is only offered once a food is selected', () => {
      openFoodCatalog();
      showAllSources();
      cy.byTestId('food-catalog-bulk').should('not.exist');
      cy.byTestId('food-catalog-approve-btn').should('not.exist');

      cy.byTestId('food-catalog-item').first().within(() => {
        cy.byTestId('food-catalog-select-checkbox').click();
      });
      cy.byTestId('food-catalog-bulk').should('be.visible').and('have.css', 'position', 'sticky');
      cy.byTestId('food-catalog-approve-btn').should('be.visible');
      cy.byTestId('food-catalog-reject-btn').should('be.visible');

      cy.byTestId('food-catalog-item').first().within(() => {
        cy.byTestId('food-catalog-select-checkbox').click();
      });
      cy.byTestId('food-catalog-bulk').should('not.exist');
    });

    it('food catalog: approving an already curated food says nothing changed', () => {
      cy.intercept('POST', '**/api/FoodCatalog/curation-status').as('setCurationStatus');
      openFoodCatalog();
      showAllSources();

      cy.get('@catalogPage').its('response.body.items').then((items: { curationStatusId: number }[]) => {
        const index = items.findIndex((item) => item.curationStatusId === CURATED_STATUS_ID);
        expect(index, 'a curated food on the first page').to.be.greaterThan(-1);

        cy.byTestId('food-catalog-item').eq(index).within(() => {
          cy.byTestId('food-catalog-select-checkbox').click();
        });
      });

      cy.byTestId('food-catalog-bulk').should('be.visible');
      cy.byTestId('food-catalog-approve-btn').click();
      cy.wait('@setCurationStatus').then(({ response }) => {
        expect(response?.statusCode).to.eq(200);
        expect(response?.body.updated).to.eq(0);
        expect(response?.body.unchanged).to.eq(1);
      });

      cy.get('.mat-mdc-snack-bar-container')
        .should('be.visible')
        .and('contain.text', 'already approved for planning');
      cy.wait('@catalogPage');
      cy.byTestId('food-catalog-bulk').should('not.exist');
    });

    it('food catalog: the USDA links section renders', () => {
      cy.intercept('GET', '**/api/FoodCatalog/proposals?*', (req) => {
        const batch = new URL(req.url).searchParams.get('batch');
        if (batch === 'fdc-attach') req.alias = 'attachProposals';
        if (batch === 'fdc-link') req.alias = 'linkProposals';
      });
      openFoodCatalog();
      cy.wait(['@attachProposals', '@linkProposals']).then((interceptions) => {
        const pendingLinks = interceptions.reduce((count, i) => count + ((i.response?.body as unknown[])?.length ?? 0), 0);
        if (pendingLinks === 0) {
          cy.byTestId('food-catalog-links-empty').scrollIntoView().should('be.visible');
          cy.byTestId('food-catalog-links-toolbar').should('not.exist');
        } else {
          cy.byTestId('food-catalog-links-toolbar').scrollIntoView().should('be.visible');
          cy.byTestId('food-catalog-links-approve-btn').should('be.disabled');
          cy.byTestId('food-catalog-links').should('be.visible');
          cy.byTestId('food-catalog-link-item').should('have.length', pendingLinks);
        }
      });
      cy.get(anyOf(['food-catalog-proposals', 'food-catalog-proposals-empty'])).scrollIntoView().should('be.visible');
    });
  });
};

selectedViewports().forEach((viewport) => adminJourney(viewport.name, viewport));
